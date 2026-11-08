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
    /// - 넣을 수: ScavSchedule.Tick(경과 초, BotsController._maxCount, AliveLoadingDelayedBotsCount, SpawnDelaysService.WaitCount,
    ///   이 레이드에서 넣은 스캐브 ÷ 사람 수, 사람 수).
    /// - 스캐브: 저격 구역이 아닌 구역(BotSpawner.SpawnZones(false), BotSpawner.cs:655) 중 그 구역의 지점(BotZone.SpawnPoints) 하나라도
    ///   모든 사람과 ScavDist 이상(시작 보호 중이면 ProtectRadius 이상)이고 자리가 남은(BotZone.HaveFreeSpace) 구역을 웹 구역 가중치로 섞어
    ///   1마리씩 BotsController.ActivateBotsByWave(SpawnWave)(BotsController.cs:499). 구역 안의 실제 지점은 바닐라가 고른다.
    /// - PMC 차례(무한 방식, pmcTurn): RefillGroups의 다음 분대를 BotsController.ActivateBotsByWave(BossLocationSpawn)(BotsController.cs:507)로.
    ///   지점·동시 그룹 제한·대기열 만료·Spawned 기록은 Task 5 패치가 한다. 분대가 맵 상한에 안 들어가면(BotSpawner.CheckOnMax, 바닐라
    ///   BotBossSpawn.Spawn과 같은 판정) 그 차례는 건너뛰고 분대는 다음 PMC 차례로 남긴다(대기열에 쌓지 않음, 설계 4.6).
    ///   목록을 다 썼거나 PMC 하드 스톱이 지났으면 그 차례는 스캐브 차례로 쓴다.
    /// - Dropped["cap"]: 차례가 상한에 막히면(ScavBlock.Cap 또는 PMC 분대가 안 들어감) 그 구간에서 한 번만 센다. 무엇이든 넣으면 구간이 끝난다.
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
                Log.Info($"{c.S.MapId} 스캐브 관리 시작(바닐라 자동 스폰 대신){(c.S.Inf != null ? ", 무한 보충 PMC 분대 " + (c.S.RefillGroups?.Count ?? 0) + "개" : "")}");
            }
            if (now < st.NextTick) return;
            st.NextTick = now + TickSec;

            if (bc == null && Singleton<IBotGame>.Instantiated) bc = Singleton<IBotGame>.Instance.BotsController;
            var spawner = bc?.BotSpawner;
            if (bc == null || spawner == null || !bc._canSpawn) return;

            CollectHumans(st.Humans);
            int players = Math.Max(1, st.Humans.Count);
            int alive = bc.AliveLoadingDelayedBotsCount;
            int pending = spawner.SpawnDelaysService?.WaitCount ?? 0;

            int n = st.Schedule.Tick(now, bc._maxCount, alive, pending, st.ScavSpawned / players, players, out bool pmcTurn);
            if (n <= 0)
            {
                if (st.Schedule.LastBlock == ScavBlock.Cap) MarkCap(c, st);
                return;
            }

            if (pmcTurn)
            {
                var r = TryPmc(c, st, bc, spawner, now);
                if (r == PmcResult.Spawned) { st.CapEpisode = false; return; }
                if (r == PmcResult.NoRoom) { MarkCap(c, st); return; }
                // Unavailable: 이 차례는 스캐브로
            }

            int done = SpawnScavs(c, st, bc, spawner, n, now, difficulties);
            if (done > 0) st.CapEpisode = false;
        }

        private static PmcResult TryPmc(RaidContext c, State st, BotsController bc, BotSpawner spawner, float now)
        {
            var groups = c.S.RefillGroups;
            if (groups == null || st.RefillIndex >= groups.Count)
            {
                Log.InfoOnce("ScavDirector.RefillDone:" + c.S.Seed, $"{c.S.MapId} 보충 PMC 분대 목록을 다 씀({groups?.Count ?? 0}개): 남은 차례는 스캐브만");
                return PmcResult.Unavailable;
            }
            if (SpawnExpiry.PastHardStop(now, c.S.HardStopPmcSec)) return PmcResult.Unavailable;

            var g = groups[st.RefillIndex];
            spawner.CheckOnMax(Math.Max(1, g.Size), out int toDelay, out _);
            if (toDelay > 0) return PmcResult.NoRoom;

            st.RefillIndex++;
            bc.ActivateBotsByWave(WaveFactory.Pmc(g, ""));
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
                Log.InfoOnce("ScavDirector.NoZone:" + s.Seed, $"{s.MapId} 스캐브를 넣을 구역이 없음(거리·보호·구역 자리): 이번 차례 건너뜀");
                return 0;
            }

            double share = s.Placement == "hotspot" ? 1.0 : s.Placement == "mixed" ? s.HotspotShare : 0;
            var w = s.ZoneWeights;
            var order = WeightedOrder.Shuffle(st.Eligible, z => w == null ? 1 : w.GetValueOrDefault(z.NameZone, 1), share, c.Rng);

            for (int i = 0; i < n; i++)
            {
                var zone = order[i % order.Count];
                var d = difficulties != null && difficulties.Count > 0 ? difficulties.Random() : BotDifficulty.normal;
                bool pscav = c.Rng.NextDouble() < s.Scav.PscavChance;
                Watch(bc.ActivateBotsByWave(WaveFactory.Scav(zone.NameZone, d, pscav)));
            }
            st.ScavSpawned += n;
            c.Spawned["assault"] += n;
            return n;
        }

        private static void MarkCap(RaidContext c, State st)
        {
            if (st.CapEpisode) return;
            st.CapEpisode = true;
            c.Dropped["cap"]++;
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

        /// <summary>살아 있는 사람 플레이어 위치(버퍼 재사용).</summary>
        private static void CollectHumans(List<Vec3> into)
        {
            into.Clear();
            var gw = Singleton<GameWorld>.Instance;
            if (gw == null) return;
            var alive = gw.AllAlivePlayersList;
            for (int i = 0; i < alive.Count; i++)
            {
                var p = alive[i];
                if (p == null || p.IsAI) continue;
                Vector3 v = p.Position;
                into.Add(new Vec3(v.x, v.y, v.z));
            }
        }

        /// <summary>비동기 생성(프로필 받기)에서 난 예외를 한 번만 남긴다.</summary>
        private static void Watch(Task t)
        {
            if (t == null) return;
            t.ContinueWith(x => Log.ErrorOnce("ScavDirector.Spawn", x.Exception!.GetBaseException()),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
