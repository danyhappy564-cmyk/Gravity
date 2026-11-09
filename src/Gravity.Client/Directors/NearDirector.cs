using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using Gravity.Client.Patches;
using Gravity.Shared;
using UnityEngine;

namespace Gravity.Client.Directors
{
    /// <summary>
    /// F8 근처 보충: 사람 플레이어(GameWorld.MainPlayer)에서 [Near.MinDist, Near.MaxDist] 띠 안의 지점에 봇을 넣는다.
    /// NonWavesPatch가 매 프레임 부르지만 일은 레이드 시간 2초에 한 번만 한다(Ruling 9). 봇은 지우지 않는다(ORBIT 호환).
    /// 스캐브 관리(Scav.Managed)와 상관없이 Near.On이면 돈다(연구소처럼 스캐브가 바닐라인 맵에서도).
    /// - 언제·무엇: NearSchedule.Tick(경과 초, BotsController._maxCount, AliveLoadingDelayedBotsCount)이 낸 자리(NearSlot) 목록.
    ///   역할 이름은 WildSpawnType으로 바꾸고(대소문자 무시) 모르는 이름은 assault.
    /// - 어디: 자리마다 PointPicker.PickRing(그 역할의 모든 후보, 플레이어 위치, MinDist, MaxDist, 이번 차례에 고른 지점, Spacing, 시야, Rng).
    ///   후보는 레이드에서 한 번 만든다.
    ///   스캐브·사용자 지정 역할 = 저격 아닌 구역(BotSpawner.SpawnZones(false))의 지점(BotZone.SpawnPoints). 웨이브에 그 지점의 구역 이름과
    ///   PerfectPos를 주면 바닐라 SelectAISpawnPoints가 그 구역 지점을 PerfectPos에 가까운 순으로 고르므로(SpawnSystem.cs:978-983)
    ///   고른 지점 자체가 1순위다.
    ///   PMC = PMC 후보 지점(RaidContext.ZonePoints, PmcZones.EnsurePoints). 바닐라 PMC 경로는 PerfectPos를 안 보므로
    ///   PmcPointsPatch가 근처 보충 웨이브면 고른 지점을 그대로 넣는다(RaidContext.NearWaves).
    /// - 시야(Near.Sight): 시도마다 고른 후보 1개만 Physics.Linecast(눈 위치 → 지점 + 1.5m, 지형·하이폴리 벽 레이어)로 본다.
    ///   막힘이 없으면(보이면) 그 시도는 실패하고 다시 섞어 고른다(Near.Attempts번까지). 후보 전체를 훑지 않는다.
    /// - 맵 상한: 차례 처음에 BotSpawner.CheckOnMax로 빈자리를 읽고 넣을 때마다 줄인다. 무리가 안 들어가면 그 자리는 Dropped["nearCap"]. 바닐라가 대기열(DelayBossSpawn)에
    ///   넣었다가 나중에 낡은 위치에 내는 일은 PmcCapDelayPatch가 막는다(근처 보충 웨이브는 대기열에 넣지 않고 버림).
    /// - 지점을 못 찾으면 그 자리는 건너뛰고 Dropped["noPoint"]. 넣으면 Spawned["near"] += 무리 크기(요청 기준, 스캐브 assault와 같음).
    /// </summary>
    internal static class NearDirector
    {
        private const float TickSec = 2f;
        private const float TargetLift = 1.5f;
        private const int RoomProbe = 1000;

        /// <summary>근처 보충 웨이브 표시. PointId = PMC 후보 지점 Id(RaidContext.PointById), 스캐브·사용자 지정 역할이면 -1.</summary>
        internal sealed class Tag
        {
            public readonly int PointId;
            public readonly int Size;

            public Tag(int pointId, int size)
            {
                PointId = pointId;
                Size = size;
            }
        }

        internal sealed class State
        {
            public readonly NearSchedule Schedule;
            public float NextTick;
            public bool ScavBuilt;
            public bool PmcBuilt;

            /// <summary>스캐브·사용자 지정 역할 후보(저격 아닌 구역 지점). Id → 구역.</summary>
            public readonly List<Candidate> ScavCands = new List<Candidate>();
            public readonly Dictionary<int, BotZone> ScavZone = new Dictionary<int, BotZone>();

            /// <summary>PMC 후보(RaidContext.ZonePoints를 펼친 것). Id → 가장 가까운 구역.</summary>
            public readonly List<Candidate> PmcCands = new List<Candidate>();
            public readonly Dictionary<int, BotZone> PmcZone = new Dictionary<int, BotZone>();

            // 차례마다 다시 쓰는 버퍼
            public readonly List<Vec3> Taken = new List<Vec3>();
            public readonly List<Vec3> Seen = new List<Vec3>();

            public State(NearSchedule schedule) => Schedule = schedule;
        }

        /// <summary>NonWavesPatch에서 매 프레임 부른다. 2초가 안 지났으면 시각 비교만 하고 끝난다.</summary>
        internal static void Tick(RaidContext c, BotsController? bc)
        {
            if (!c.Started) return;
            var n = c.S.Near;
            if (n == null || !n.On) return;
            float now = c.Elapsed;
            var st = c.NearState;
            if (st == null)
            {
                st = c.NearState = new State(new NearSchedule(n, c.Rng));
                Log.Info($"{c.S.MapId} 근처 보충 시작: 첫 차례 {n.FirstDelay:0}초, 간격 {n.Interval:0}초, 거리 {n.MinDist:0}~{n.MaxDist:0}m, 시야 {(n.Sight ? "켬" : "끔")}");
            }
            if (now < st.NextTick) return;
            st.NextTick = now + TickSec;

            if (bc == null && Singleton<IBotGame>.Instantiated) bc = Singleton<IBotGame>.Instance.BotsController;
            var spawner = bc?.BotSpawner;
            if (bc == null || spawner == null || !bc._canSpawn) return;

            var gw = Singleton<GameWorld>.Instance;
            if (gw == null) return;
            var player = gw.MainPlayer;
            if (player == null || player.HealthController == null || !player.HealthController.IsAlive) return;

            var slots = st.Schedule.Tick(now, bc._maxCount, bc.AliveLoadingDelayedBotsCount);
            if (slots.Count == 0) return;

            Vector3 pp = player.Position;
            var playerPos = new Vec3(pp.x, pp.y, pp.z);
            Vector3 eye = EyePosition(player);
            st.Taken.Clear();
            st.Seen.Clear();
            // 맵 상한 빈자리는 차례 처음에 한 번 읽고 넣을 때마다 줄인다. 생성은 비동기라 같은 차례 안에서는 BotSpawner 수가 아직 안 늘어서
            // 자리마다 CheckOnMax를 다시 부르면 상한을 넘겨 넣을 수 있다. MaxBots가 0(상한 없음)이면 RoomProbe 그대로.
            spawner.CheckOnMax(RoomProbe, out _, out int room);

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var role = ParseRole(slot.Role);
                int size = Math.Max(1, slot.Count);
                bool pmc = role.IsPmcBot();

                if (size > room)
                {
                    c.Dropped["nearCap"]++;
                    continue;
                }

                List<Candidate> pool;
                Dictionary<int, BotZone> zoneOf;
                if (pmc)
                {
                    if (!EnsurePmc(c, st)) { c.Dropped["noPoint"]++; continue; }
                    pool = st.PmcCands;
                    zoneOf = st.PmcZone;
                }
                else
                {
                    EnsureScav(c, st, spawner);
                    pool = st.ScavCands;
                    zoneOf = st.ScavZone;
                }

                int id = Pick(c, st, n, pool, playerPos, eye);
                if (id < 0 || !zoneOf.TryGetValue(id, out var zone) || zone == null)
                {
                    c.Dropped["noPoint"]++;
                    continue;
                }

                Vec3 pos = Find(pool, id);
                st.Taken.Add(pos);
                var wave = WaveFactory.AtPoint(role, size, pos, zone.NameZone);
                c.NearWaves.Add(wave, new Tag(pmc ? id : -1, size));
                bc.ActivateBotsByWave(wave);
                room -= size;
                c.Spawned["near"] += size;
            }
        }

        /// <summary>
        /// 한 자리의 지점 Id(없으면 -1). 시야를 보면 시도마다 Linecast는 1번만: 이번 시도에서 처음 판정하는 후보만 실제로 쏘고,
        /// 그 뒤 후보는 "보임"으로 쳐서 PickRing이 실패하게 한다(다음 시도에서 다시 섞음). 이번 차례에 보인다고 나온 지점은 쏘지 않고 뺀다.
        /// 시야를 안 보면 PickRing이 결정적으로 거르므로 한 번만 부른다.
        /// </summary>
        private static int Pick(RaidContext c, State st, NearDto n, List<Candidate> pool, Vec3 player, Vector3 eye)
        {
            if (pool.Count == 0) return -1;
            int attempts = n.Sight ? Math.Max(1, n.Attempts) : 1;
            for (int a = 0; a < attempts; a++)
            {
                Func<Vec3, bool>? visible = null;
                if (n.Sight)
                {
                    bool used = false;
                    visible = p =>
                    {
                        for (int s = 0; s < st.Seen.Count; s++)
                            if (st.Seen[s] == p) return true;
                        if (used) return true;
                        used = true;
                        var target = new Vector3(p.X, p.Y + TargetLift, p.Z);
                        bool seen = !Physics.Linecast(eye, target, LayersMaskController.HighPolyWithTerrainMask);
                        if (seen) st.Seen.Add(p);
                        return seen;
                    };
                }
                var r = PointPicker.PickRing(pool, player, n.MinDist, n.MaxDist, st.Taken, n.Spacing, visible, c.Rng);
                if (r.Ids.Count > 0) return r.Ids[0];
            }
            return -1;
        }

        private static Vec3 Find(List<Candidate> pool, int id)
        {
            for (int i = 0; i < pool.Count; i++)
                if (pool[i].Id == id) return pool[i].Pos;
            return default;
        }

        /// <summary>눈 위치: 카메라 뼈("Cam") → 머리 뼈 → 발 위치 + 1.6m 순.</summary>
        private static Vector3 EyePosition(Player player)
        {
            var cam = player.CameraPosition;
            if (cam != null) return cam.position;
            var head = player.PlayerBones?.Head;
            if (head != null) return head.position;
            return player.Position + Vector3.up * 1.6f;
        }

        /// <summary>NearSlot.Role → WildSpawnType(대소문자 무시). 숫자나 모르는 이름이면 assault(한 번 경고).</summary>
        internal static WildSpawnType ParseRole(string? name)
        {
            // 서버가 "pmc"(무작위 PMC)를 보내면 자리마다 진영을 반반으로 고른다.
            if (string.Equals(name, "pmc", StringComparison.OrdinalIgnoreCase))
                return UnityEngine.Random.value < 0.5f ? WildSpawnType.pmcUSEC : WildSpawnType.pmcBEAR;
            if (!string.IsNullOrWhiteSpace(name) && !char.IsDigit(name![0]) && name[0] != '-'
                && Enum.TryParse<WildSpawnType>(name, true, out var r) && Enum.IsDefined(typeof(WildSpawnType), r))
                return r;
            Log.WarnOnce("NearDirector.Role:" + name, $"근처 보충 역할 '{name}'을 모름: assault로 넣음");
            return WildSpawnType.assault;
        }

        /// <summary>스캐브·사용자 지정 역할 후보: 저격 아닌 구역의 지점을 한 번 모은다.</summary>
        private static void EnsureScav(RaidContext c, State st, BotSpawner spawner)
        {
            if (st.ScavBuilt) return;
            st.ScavBuilt = true;
            int id = 0, zones = 0;
            foreach (var z in spawner.SpawnZones(false))
            {
                if (z == null) continue;
                var sps = z.SpawnPoints;
                if (sps == null || sps.Length == 0) continue;
                zones++;
                for (int i = 0; i < sps.Length; i++)
                {
                    if (sps[i] == null) continue;
                    id++;
                    st.ScavCands.Add(new Candidate(id, PmcZones.V(sps[i].Position), false));
                    st.ScavZone[id] = z;
                }
            }
            Log.Info($"{c.S.MapId} 근처 보충 스캐브 후보 지점 {id}개(저격 제외 구역 {zones}개)");
        }

        /// <summary>PMC 후보: RaidContext.ZonePoints를 한 번 펼친다. 아직 못 만들면 false(다음 차례에 다시).</summary>
        private static bool EnsurePmc(RaidContext c, State st)
        {
            if (st.PmcBuilt) return true;
            if (!PmcZones.EnsurePoints(c)) return false;
            st.PmcBuilt = true;
            foreach (var kv in c.ZonePoints)
            {
                if (kv.Key == null) continue;
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    st.PmcCands.Add(kv.Value[i]);
                    st.PmcZone[kv.Value[i].Id] = kv.Key;
                }
            }
            Log.Info($"{c.S.MapId} 근처 보충 PMC 후보 지점 {st.PmcCands.Count}개");
            return true;
        }
    }
}
