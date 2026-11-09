using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using Gravity.Shared;
using UnityEngine;

namespace Gravity.Client.Directors
{
    /// <summary>
    /// F6·F7: 바닐라 웨이브 밖 자동 스폰(NonWavesSpawnScenario.Update) 대신 스캐브를 넣고, 무한 보충 맵이면 PMC 분대도 같은 루프에서 넣는다.
    /// NonWavesPatch가 매 프레임 부르지만 일은 레이드 시간 2초에 한 번만 한다(Ruling 9). 봇은 지우지 않는다(ORBIT 호환).
    /// - 넣을 수: ScavSchedule.Tick(경과 초, BotsController._maxCount, AliveLoadingDelayedBotsCount, 살아 있는 스캐브(assault) 수,
    ///   SpawnDelaysService.WaitCount, 이 레이드에서 넣은 스캐브 ÷ 사람 수, 사람 수). 스캐브 빈자리 = min(cap - 살아 있는+로딩+대기 봇,
    ///   softCap - 살아 있는 스캐브)(웹 "이 수를 넘으면 보충 멈춤", 컨트롤러 결정 10). PMC 차례는 맵 상한만 보고, 휴식 창·스캐브 하드 스톱·
    ///   대기 수·플레이어당 한도·softCap은 스캐브에만 걸린다(결정 10·13).
    /// - 스캐브: 저격 구역이 아닌 구역(BotSpawner.SpawnZones(false), BotSpawner.cs:655) 중 그 구역의 지점(BotZone.SpawnPoints) 하나라도
    ///   모든 사람과 ScavDist 이상(시작 보호 중이면 ProtectRadius 이상)이고 자리가 남은(BotZone.HaveFreeSpace) 구역을 웹 구역 가중치로 섞어
    ///   1마리씩 BotsController.ActivateBotsByWave(SpawnWave)(BotsController.cs:499). 구역 안의 실제 지점은 바닐라가 고른다.
    /// - PMC 차례(무한 방식, pmcTurn): RefillGroups의 다음 분대를 BotsController.ActivateBotsByWave(BossLocationSpawn)(BotsController.cs:507)로.
    ///   지점·동시 그룹 제한·대기열 만료·Spawned 기록은 Task 5 패치가 한다. 분대가 맵 상한에 안 들어가면(1.0: RaidContext.TryReserve —
    ///   생존·로딩·대기·Gravity 예약, 상한 제외 팩션 뺌) 그 차례는 건너뛰고 분대는 다음 PMC 차례로 남긴다(대기열에 쌓지 않음, 설계 4.6).
    ///   "레이드 전체" 그룹 제한(RaidContext.Groups)에 걸린 분대는 버리고 Dropped["groupCap"](1.0).
    ///   목록을 다 썼거나 PMC 하드 스톱이 지났거나 미궁 PMC가 꺼져 있으면(allowPmcLabyrinth=false, 결정 12) 그 차례는 스캐브 차례로 쓴다
    ///   (스캐브 판정을 거친 수만큼).
    /// - 플레이어 스캐브 확률(scav.pscavChance)은 0.2에서 적용하지 않는다: SpawnWave.IsPlayers를 바닐라가 읽지 않는다(결정 14, WaveFactory.Scav).
    /// - Dropped["scavCap"]: 차례가 맵 상한에 막히면(ScavBlock.Cap 또는 PMC 분대가 안 들어감) 그 구간에서 한 번만 센다. 무엇이든 넣으면 구간이 끝난다.
    ///   softCap(살아 있는 스캐브 한도)에 막힌 것은 버림이 아니므로 세지 않는다.
    /// </summary>
    internal static class ScavDirector
    {
        private const float TickSec = 2f;

        internal sealed class State
        {
            public readonly ScavSchedule Schedule;
            public float NextTick;
            public int RefillIndex;
            public int ScavSpawned;
            public bool CapEpisode;
            public bool ZonesBuilt;
            /// <summary>PMC만 보충할 때(스캐브 관리 꺼짐) 다음 차례 시각.</summary>
            public float PmcOnlyNext;
            public float ExtractNext;

            /// <summary>
            /// PMC 우선 줄(사용자 결정 11-10, ABPS 비교 추천안 나): 보충 PMC 분대가 상한에 막히면 그 분대 인원을 "대기 중" 예약(PendingKey)으로
            /// 잡아 두고 2초마다 다시 시도한다. 그동안 스캐브·근처 보충은 그 자리를 못 쓰므로, 자리가 모이면 PMC가 먼저 나온다.
            /// </summary>
            public readonly object PendingKey = new object();
            public int PendingSize;
            public bool PendingExtract;
            public int LastSize;

            /// <summary>저격 아닌 구역과 그 구역 지점 위치(레이드에서 한 번 만든다).</summary>
            public readonly List<KeyValuePair<BotZone, Vec3[]>> Zones = new List<KeyValuePair<BotZone, Vec3[]>>();

            // 차례마다 다시 쓰는 버퍼
            public readonly List<BotZone> Eligible = new List<BotZone>();
            public readonly List<Vec3> Humans = new List<Vec3>();

            public State(ScavSchedule schedule) => Schedule = schedule;
        }

        private enum PmcResult { Spawned, NoRoom, Unavailable }

        /// <summary>NonWavesPatch에서 매 프레임 부른다. 2초가 안 지났으면 시각 비교만 하고 끝난다.</summary>
        internal static void Tick(RaidContext c, BotsController? bc, WDictionary<BotDifficulty>? difficulties)
        {
            if (!c.Started) return;
            float now = c.Elapsed;
            var st = c.ScavState;
            if (st == null)
            {
                st = c.ScavState = new State(new ScavSchedule(c.S.Scav, c.S.Inf, c.S.HardStopScavSec, c.Rng));
                Log.Info($"{c.S.MapId} 스캐브 관리 시작(바닐라 자동 스폰 대신){(c.S.Inf != null ? ", 무한 보충 PMC 분대 " + c.Refill.Count + "개" : "")}");
            }
            if (now < st.NextTick) return;
            st.NextTick = now + TickSec;

            if (bc == null && Singleton<IBotGame>.Instantiated) bc = Singleton<IBotGame>.Instance.BotsController;
            var spawner = bc?.BotSpawner;
            if (bc == null || spawner == null || !bc._canSpawn) return;

            if (TryExtract(c, st, bc, spawner, now)) return;
            if (RetryPending(c, st, bc, spawner, now)) return;

            int aliveScavs = CollectPlayers(st.Humans, c, out int excluded);
            int players = Math.Max(1, st.Humans.Count);
            // 맵 상한 판정용 점유: 게임 집계(생존·로딩·대기) − 상한 제외 팩션 + 아직 집계에 안 잡힌 Gravity 예약(Codex 검토 1번).
            int alive = Math.Max(0, RaidContext.Occupied(spawner) - excluded) + c.Ledger.Held(Time.time);
            int pending = spawner.SpawnDelaysService?.WaitCount ?? 0;

            int n = st.Schedule.Tick(now, bc._maxCount, alive, aliveScavs, pending, aliveScavs / players, players, out bool pmcTurn);

            // PMC 차례는 스캐브 판정(n)과 상관없이 먼저 본다. n은 PMC를 못 넣을 때 대신 넣을 스캐브 수다.
            if (pmcTurn)
            {
                var r = TryPmc(c, st, bc, spawner, now);
                if (r == PmcResult.Spawned) { st.CapEpisode = false; return; }
                if (r == PmcResult.NoRoom) { MarkCap(c, st); return; }
                // Unavailable: 이 차례는 스캐브로
            }

            if (n <= 0)
            {
                if (st.Schedule.LastBlock == ScavBlock.Cap) MarkCap(c, st);
                return;
            }

            int done = SpawnScavs(c, st, bc, spawner, n, now, difficulties);
            if (done > 0) st.CapEpisode = false;
        }

        /// <summary>
        /// 스캐브 관리를 끈 맵(연구소 allowScavLab 꺼짐 등)이 무한 방식이면 PMC 차례만 돌린다(0.3). 스캐브는 바닐라 자동 스폰 그대로.
        /// 차례 간격 = inf.delay ± jitter, 차례마다 inf.pmcShare 확률로 보충 PMC 분대 하나. 맵 상한에 막히면 차례를 쓰지 않고 다음 틱에 다시.
        /// </summary>
        internal static void TickPmcOnly(RaidContext c, BotsController? bc)
        {
            if (!c.Started || (c.S.Inf == null && c.ExtractQueue.Count == 0) || (c.Refill.Count == 0 && c.ExtractQueue.Count == 0)) return;
            float now = c.Elapsed;
            var st = c.ScavState;
            if (st == null)
            {
                st = c.ScavState = new State(new ScavSchedule(c.S.Scav, c.S.Inf, c.S.HardStopScavSec, c.Rng));
                // PMC 첫 보충은 서버가 보낸 첫 웨이브 지연부터(옛 서버는 0, Codex 검토 3번).
                st.PmcOnlyNext = (float)(c.S.Inf?.FirstDelaySec ?? 0);
                Log.Info($"{c.S.MapId} 스캐브는 바닐라, 무한 보충 PMC만(분대 {c.Refill.Count}개)");
            }
            if (now < st.NextTick) return;
            st.NextTick = now + TickSec;

            if (bc == null && Singleton<IBotGame>.Instantiated) bc = Singleton<IBotGame>.Instance.BotsController;
            var spawner = bc?.BotSpawner;
            if (bc == null || spawner == null || !bc._canSpawn) return;

            if (TryExtract(c, st, bc, spawner, now)) return;
            if (RetryPending(c, st, bc, spawner, now)) return;
            if (c.S.Inf == null || now < st.PmcOnlyNext) return;

            var inf = c.S.Inf;
            if (c.Rng.NextDouble() < inf.PmcShare)
            {
                var r = TryPmc(c, st, bc, spawner, now);
                if (r == PmcResult.NoRoom) { MarkCap(c, st); return; }
                if (r == PmcResult.Spawned) st.CapEpisode = false;
            }
            double j = inf.Jitter;
            st.PmcOnlyNext = now + (float)(inf.DelaySec * (1.0 + (j > 0 ? (c.Rng.NextDouble() * 2.0 - 1.0) * j : 0.0)));
        }

        /// <summary>활동/휴식·PMC 비율과 무관하게 대기열을 1명씩 처리한다. 매 틱 최대 1회, 별도 반복문은 없다.</summary>
        private static bool TryExtract(RaidContext c, State st, BotsController bc, BotSpawner spawner, float now)
        {
            if (c.ExtractQueue.Count == 0) return false;
            if (!ExtractRefillRule.CanQueue(c.S, true, 0, now))
            {
                c.ExtractQueue.Clear();
                if (st.PendingExtract) ClearPending(c, st);
                return false;
            }
            if (now < st.ExtractNext) return false;
            var r = TryPmc(c, st, bc, spawner, now, true);
            if (r == PmcResult.NoRoom) { MarkCap(c, st); return true; }
            if (r != PmcResult.Spawned) return false;
            st.CapEpisode = false;
            st.ExtractNext = now + (float)(c.S.Inf?.DelaySec ?? 45);
            return true;
        }

        /// <summary>줄 서 있는 보충 분대(탈출 보충 제외 — 그쪽은 TryExtract가 매 틱 다시 본다)를 2초마다 다시 넣어 본다. 막혀 있으면 이 틱은 끝(true).</summary>
        private static bool RetryPending(RaidContext c, State st, BotsController bc, BotSpawner spawner, float now)
        {
            if (st.PendingSize <= 0 || st.PendingExtract) return false;
            var r = TryPmc(c, st, bc, spawner, now);
            if (r == PmcResult.Spawned) { st.CapEpisode = false; return true; }
            if (r == PmcResult.NoRoom) { MarkCap(c, st); return true; }
            return false;
        }

        private static void ClearPending(RaidContext c, State st)
        {
            c.Ledger.Release(st.PendingKey);
            st.PendingSize = 0;
            st.PendingExtract = false;
        }

        /// <summary>보충 PMC 분대 하나를 넣어 본다. 상한에 막히면 그 분대 몫을 대기 중 예약으로 잡아 PMC 우선 줄에 세운다.</summary>
        private static PmcResult TryPmc(RaidContext c, State st, BotsController bc, BotSpawner spawner, float now, bool extractOnly = false)
        {
            // 줄 서 있던 내 몫은 자리 계산에서 빼고 다시 본다(내 예약이 나를 막지 않게)
            c.Ledger.Release(st.PendingKey);
            st.LastSize = 0;
            var r = TryPmcCore(c, st, bc, spawner, now, extractOnly);
            if (r == PmcResult.NoRoom && st.LastSize > 0)
            {
                st.PendingSize = st.LastSize;
                st.PendingExtract = extractOnly && c.ExtractQueue.Count > 0;
                c.Ledger.Hold(st.PendingKey, st.PendingSize, true, Time.time);
            }
            else
            {
                st.PendingSize = 0;
                st.PendingExtract = false;
            }
            return r;
        }

        private static PmcResult TryPmcCore(RaidContext c, State st, BotsController bc, BotSpawner spawner, float now, bool extractOnly)
        {
            if (c.S.MapId == "labyrinth" && !c.S.AllowPmcLabyrinth)
            {
                Log.InfoOnce("ScavDirector.LabPmcOff:" + c.S.Seed, "labyrinth 미궁 PMC 꺼짐(allowPmcLabyrinth): 보충 PMC 없이 스캐브만");
                return PmcResult.Unavailable;
            }
            var groups = c.Refill;
            bool extra = extractOnly && c.ExtractQueue.Count > 0;
            if (!extra && st.RefillIndex >= groups.Count)
            {
                Log.InfoOnce("ScavDirector.RefillDone:" + c.S.Seed, $"{c.S.MapId} 보충 PMC 분대 목록을 다 씀({groups?.Count ?? 0}개): 남은 차례는 스캐브만");
                return PmcResult.Unavailable;
            }
            if (SpawnExpiry.PastHardStop(now, c.S.HardStopPmcSec)) return PmcResult.Unavailable;

            var g = extra ? c.ExtractQueue.Peek() : groups[st.RefillIndex];
            int size = Math.Max(1, g.Size);
            // "레이드 전체" 그룹 제한: 계획 밖 분대(무한·탈출 보충)도 같은 장부로 센다(Codex 검토 2번).
            // 초과 처리가 split이면 서버 계획과 같은 규칙으로 작은 분대로 나눠 이번 차례에 첫 조각, 나머지는 목록 바로 뒤에(재검토 2번).
            if (!c.Groups.CanAdd(size))
            {
                var parts = extra ? new List<int>() : c.Groups.Split(size);
                if (parts.Count == 0)
                {
                    if (extra) c.ExtractQueue.Dequeue();
                    else st.RefillIndex++;
                    c.Dropped["groupCap"]++;
                    Log.InfoOnce("ScavDirector.RaidGroupCap:" + c.S.Seed, $"{c.S.MapId} 레이드 전체 그룹 제한에 걸린 보충 분대({size}명)는 넣지 않음");
                    return PmcResult.Unavailable;
                }
                groups[st.RefillIndex] = g = g with { Size = parts[0] };
                for (int i = parts.Count - 1; i >= 1; i--) groups.Insert(st.RefillIndex + 1, g with { Size = parts[i] });
                Log.Verbose($"{c.S.MapId} 레이드 전체 그룹 제한: 보충 분대 {size}명 → {string.Join("+", parts)}명으로 나눔");
                size = parts[0];
            }
            // 맵 상한: 생존·로딩·대기·예약을 모두 세고 제외 팩션을 뺀 빈자리에 분대가 들어가야 예약하고 요청한다(Codex 검토 1번).
            var wave = WaveFactory.Pmc(g, "");
            st.LastSize = size;
            if (!c.TryReserve(bc, wave, size)) return PmcResult.NoRoom;
            c.Owned.Add(wave, wave);

            c.Groups.TryUse(size);
            if (extra) c.ExtractQueue.Dequeue();
            else st.RefillIndex++;
            bc.ActivateBotsByWave(wave);
            return PmcResult.Spawned;
        }

        private static int SpawnScavs(RaidContext c, State st, BotsController bc, BotSpawner spawner, int n, float now,
            WDictionary<BotDifficulty>? difficulties)
        {
            EnsureZones(st, spawner, c);
            var s = c.S;
            bool protect = s.ProtectOn && now < s.ProtectSec;
            st.Eligible.Clear();
            for (int i = 0; i < st.Zones.Count; i++)
            {
                var kv = st.Zones[i];
                if (kv.Key == null || !kv.Key.HaveFreeSpace(1)) continue;
                if (PointPicker.HasClearPoint(kv.Value, st.Humans, s.ScavDist, protect, s.ProtectRadius)) st.Eligible.Add(kv.Key);
            }
            if (st.Eligible.Count == 0)
            {
                // 완화: 좁은 맵에서 모든 지점이 거리 안이면 거리를 절반으로 한 번 더 본다(구역 자리·시작 보호는 그대로).
                for (int i = 0; i < st.Zones.Count; i++)
                {
                    var kv = st.Zones[i];
                    if (kv.Key == null || !kv.Key.HaveFreeSpace(1)) continue;
                    if (PointPicker.HasClearPoint(kv.Value, st.Humans, s.ScavDist * 0.5, protect, s.ProtectRadius)) st.Eligible.Add(kv.Key);
                }
                if (st.Eligible.Count > 0) Log.InfoOnce("ScavDirector.Relax:" + s.Seed, $"{s.MapId} 스캐브 거리 {s.ScavDist:0}m 안에 구역뿐: 절반 거리로 넣음");
            }
            if (st.Eligible.Count == 0)
            {
                Log.InfoOnce("ScavDirector.NoZone:" + s.Seed, $"{s.MapId} 스캐브를 넣을 구역이 없음(거리·보호·구역 자리): 이번 차례 건너뜀");
                return 0;
            }

            double share = s.Placement == "hotspot" ? 1.0 : s.Placement == "mixed" ? s.HotspotShare : 0;
            // 옛 서버가 새 필드를 보내지 않으면 기존 공용 비중을 그대로 사용합니다.
            var w = s.ScavZoneWeights ?? s.ZoneWeights;
            // 설정에 없는 구역은 기본 비중 100(Codex 검토 4번, 서버도 같은 값으로 채워 보낸다).
            var order = WeightedOrder.Shuffle(st.Eligible, z => ZoneWeight.Of(w, z.NameZone), share, c.Rng);

            int done = 0;
            for (int i = 0; i < n; i++)
            {
                // 스캐브 요청도 게임이 프로필을 받는 동안 다 세지 않으므로 1명씩 예약하고, 생성 Task가 끝나면 푼다(Codex 검토 1번).
                // 예약 키 = 웨이브. 웨이브를 기억해 두면 그 생성 자료의 게임 상한 검사에도 팩션 제외가 걸린다(재검토 3번, NativeCapPatch).
                var zone = order[i % order.Count];
                var d = difficulties != null && difficulties.Count > 0 ? difficulties.Random() : BotDifficulty.normal;
                // 플레이어 스캐브(pscavChance)는 0.2에서 적용하지 않는다(IsPlayers를 바닐라가 읽지 않음, 결정 14).
                var wave = WaveFactory.Scav(zone.NameZone, d, false);
                if (!c.TryReserve(bc, wave, 1)) break;
                c.ScavWaves.Add(wave, wave);
                Watch(c, wave, bc.ActivateBotsByWave(wave));
                done++;
            }
            st.ScavSpawned += done;
            c.Spawned["assault"] += done;
            return done;
        }

        private static void MarkCap(RaidContext c, State st)
        {
            if (st.CapEpisode) return;
            st.CapEpisode = true;
            c.Dropped["scavCap"]++;
        }

        /// <summary>저격 아닌 구역과 지점 위치를 한 번 모은다. 지점이 없는 구역은 뺀다.</summary>
        private static void EnsureZones(State st, BotSpawner spawner, RaidContext c)
        {
            if (st.ZonesBuilt) return;
            st.ZonesBuilt = true;
            int points = 0;
            foreach (var z in spawner.SpawnZones(false))
            {
                if (z == null) continue;
                if (c.IsZoneDisabled(z.NameZone)) continue;
                var sps = z.SpawnPoints;
                if (sps == null || sps.Length == 0) continue;
                var pos = new Vec3[sps.Length];
                for (int i = 0; i < sps.Length; i++)
                {
                    var p = sps[i].Position;
                    pos[i] = new Vec3(p.x, p.y, p.z);
                }
                st.Zones.Add(new KeyValuePair<BotZone, Vec3[]>(z, pos));
                points += pos.Length;
            }
            Log.Info($"{c.S.MapId} 스캐브 구역 {st.Zones.Count}개(저격 제외), 지점 {points}개");
        }

        /// <summary>살아 있는 사람 플레이어 위치를 버퍼에 담고(재사용), 살아 있는 스캐브(assault 역할 봇) 수를 돌려준다.
        /// excluded = 맵 상한에서 뺄 팩션 봇 수(서버 CapExclude, 0.4).</summary>
        private static int CollectPlayers(List<Vec3> humans, RaidContext c, out int excluded)
        {
            humans.Clear();
            excluded = 0;
            var gw = Singleton<GameWorld>.Instance;
            if (gw == null) return 0;
            int scavs = 0;
            var alive = gw.AllAlivePlayersList;
            for (int i = 0; i < alive.Count; i++)
            {
                var p = alive[i];
                if (p == null) continue;
                if (p.IsAI)
                {
                    var role = p.Profile?.Info?.Settings?.Role;
                    // 무리로 나온 스캐브는 역할이 assaultGroup이다(11-09 세관 판에서 확인): 둘 다 센다.
                    if (role != null && (role.Value == WildSpawnType.assault || role.Value == WildSpawnType.assaultGroup)) scavs++;
                    else if (role != null && c.IsCapExcluded(role.Value)) excluded++;
                    continue;
                }
                Vector3 v = p.Position;
                humans.Add(new Vec3(v.x, v.y, v.z));
            }
            return scavs;
        }

        /// <summary>비동기 생성(프로필 받기)이 끝나면 예약을 풀고, 예외는 한 번만 남긴다.</summary>
        private static void Watch(RaidContext c, object key, Task t)
        {
            if (t == null || t.IsCompleted)
            {
                c.Ledger.Release(key);
                if (t != null && t.IsFaulted) Log.ErrorOnce("ScavDirector.Spawn", t.Exception!.GetBaseException());
                return;
            }
            t.ContinueWith(x =>
            {
                c.Ledger.Release(key); // 끝났으면 게임 집계(생존·로딩·대기)에 잡혔다
                if (x.IsFaulted) Log.ErrorOnce("ScavDirector.Spawn", x.Exception!.GetBaseException());
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
