using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using Gravity.Shared;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Gravity.Client.Patches
{
    /// <summary>
    /// PMC 후보 지점·구역 준비(레이드에서 한 번). 게임 스레드에서만 부른다.
    /// 후보 지점은 게임이 레이드 시작 때 SpawnPointsCollection.CreateFromScene(BaseLocalGame.Run)으로 만든 모음을 그대로 쓴다
    /// (BossSpawner._spawnSystem._spawnPoints, 같은 ISpawnPoint 인스턴스). 못 얻으면 CreateFromScene()을 한 번 부른다.
    /// </summary>
    internal static class PmcZones
    {
        /// <summary>ZonePoints·PointById를 만든다. BotsController.BotSpawner가 아직 없으면 false(다음 기회에 다시).</summary>
        internal static bool EnsurePoints(RaidContext c)
        {
            if (c.PointsBuilt) return true;
            if (!Singleton<IBotGame>.Instantiated) return false;
            var bc = Singleton<IBotGame>.Instance.BotsController;
            var spawner = bc?.BotSpawner;
            if (bc == null || spawner == null) return false;

            IEnumerable<ISpawnPoint> all = (spawner.BossSpawner?._spawnSystem as SpawnSystem)?._spawnPoints
                ?? (IEnumerable<ISpawnPoint>)SpawnPointsCollection.CreateFromScene();
            c.PointsBuilt = true;

            int id = 0, players = 0, bots = 0;
            foreach (var p in all)
            {
                if (p == null) continue;
                var cat = p.Categories;
                bool player = cat.ContainPlayerCategory() && p.Infiltration != null;
                bool pmc = cat.ContainBotPmcCategory();
                if (!player && !pmc) continue;
                var zone = bc.GetClosestZone(p.Position, out _);
                if (zone == null) continue;
                id++;
                c.PointById[id] = p;
                if (!c.ZonePoints.TryGetValue(zone, out var list)) c.ZonePoints[zone] = list = new List<Candidate>();
                list.Add(new Candidate(id, V(p.Position), player));
                if (player) players++; else bots++;
            }
            Log.Info($"{c.S.MapId} PMC 후보 지점 {id}개(플레이어 {players}, 봇 PMC {bots}), 구역 {c.ZonePoints.Count}개");
            return true;
        }

        /// <summary>PMC 구역이 0개인 맵(공장·미궁)이면 후보가 있는 구역을 AvailableZonesPmc에 더한다(한 번).</summary>
        internal static void EnsureZones(RaidContext c, BotBossSpawn? boss)
        {
            if (c.ZonesFilled || boss == null) return;
            if (!EnsurePoints(c)) return;
            c.ZonesFilled = true;
            var pmcZones = boss.AvailableZonesPmc; // readonly 필드지만 목록 내용은 바꿀 수 있다
            if (pmcZones == null || pmcZones.Count != 0) return;
            foreach (var kv in c.ZonePoints)
                if (kv.Value.Count > 0 && kv.Key != null && !pmcZones.Contains(kv.Key)) pmcZones.Add(kv.Key);
            Log.Info($"{c.S.MapId} PMC 구역이 없는 맵: 후보 지점이 있는 구역 {pmcZones.Count}개를 PMC 구역으로 씀");
        }

        /// <summary>배치가 hotspot·mixed면 구역 목록을 웹 구역 가중치로 제자리에서 다시 섞는다.</summary>
        internal static void Reorder(RaidContext c, List<BotZone> zones)
        {
            var s = c.S;
            if (s.DisabledZones is { Count: > 0 }) zones.RemoveAll(z => z != null && c.IsZoneDisabled(z.NameZone));
            if (zones.Count < 2 || s.Placement == "vanilla") return;
            double share = s.Placement == "hotspot" ? 1.0 : s.Placement == "mixed" ? s.HotspotShare : 0;
            var w = s.ZoneWeights;
            var ordered = WeightedOrder.Shuffle(zones, z => z == null || w == null ? 1 : w.GetValueOrDefault(z.NameZone, 1), share, c.Rng);
            for (int i = 0; i < ordered.Count; i++) zones[i] = ordered[i];
        }

        /// <summary>살아 있는 플레이어·봇 위치. 사람=Human, PMC 봇=Pmc, 보스=Boss, 나머지=Scav.</summary>
        internal static List<Actor> Actors()
        {
            var res = new List<Actor>();
            var gw = Singleton<GameWorld>.Instance;
            if (gw == null) return res;
            var alive = gw.AllAlivePlayersList;
            for (int i = 0; i < alive.Count; i++)
            {
                var p = alive[i];
                if (p == null) continue;
                ActorKind kind;
                if (!p.IsAI) kind = ActorKind.Human;
                else
                {
                    var role = p.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;
                    kind = role.IsPmcBot() ? ActorKind.Pmc : role.IsBoss() ? ActorKind.Boss : ActorKind.Scav;
                }
                res.Add(new Actor(V(p.Position), kind));
            }
            return res;
        }

        internal static Vec3 V(Vector3 v) => new Vec3(v.x, v.y, v.z);
    }

    /// <summary>
    /// BotsController.Init 뒤: PMC 구역이 0개인 맵이면 후보 지점이 있는 구역으로 AvailableZonesPmc를 채운다.
    /// 계획은 BotBossSpawn 생성자 Postfix였지만, 생성자는 BotSpawner 생성자 안(BotSpawner.cs:226)에서 돌고 그때는
    /// BotsController._botSpawner(BotsController.cs:236)와 순찰 지점 표(_zonesPatrols, BotSpawner.cs:227-237)가 아직 없어
    /// GetClosestZone을 쓸 수 없다. 그래서 Init 직후에 한다. Init 때 설정이 아직 없으면 PmcTrySpawnPatch가 첫 TrySpawn 앞에서 한다.
    /// Init 뒤에 LocalGame이 BossSpawnScenario.Run(GetPmcZones())을 부르므로(LocalGame.cs:199) 시작 웨이브보다 먼저 채워진다.
    /// </summary>
    internal sealed class PmcZonesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(BotsController).GetMethod(nameof(BotsController.Init), BindingFlags.Instance | BindingFlags.Public)!;

        [PatchPostfix]
        private static void Postfix(BotsController __instance)
        {
            try
            {
                var c = RaidContext.Current;
                if (c != null) PmcZones.EnsureZones(c, __instance?.BotSpawner?.BossSpawner);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("PmcZones", ex);
            }
        }
    }

    /// <summary>BossLocationSpawn.GetPossibleZones(allZones, marked) 뒤: Gravity 분대면 구역 순서를 가중치로 다시 섞는다.</summary>
    internal sealed class PmcZoneOrderPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(BossLocationSpawn).GetMethod(nameof(BossLocationSpawn.GetPossibleZones), BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(BotZone[]), typeof(List<BotZone>) }, null)!;

        [PatchPostfix]
        private static void Postfix(BossLocationSpawn __instance, List<BotZone> __result) => PmcZoneOrder.After(__instance, __result);
    }

    /// <summary>BossLocationSpawn.GetPossibleZones(allZones, marked, closestsPos) 뒤: 위와 같다(PerfectPos가 있는 웨이브).</summary>
    internal sealed class PmcZoneOrderPerfectPosPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(BossLocationSpawn).GetMethod(nameof(BossLocationSpawn.GetPossibleZones), BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(BotZone[]), typeof(List<BotZone>), typeof(Vector3) }, null)!;

        [PatchPostfix]
        private static void Postfix(BossLocationSpawn __instance, List<BotZone> __result) => PmcZoneOrder.After(__instance, __result);
    }

    internal static class PmcZoneOrder
    {
        internal static void After(BossLocationSpawn wave, List<BotZone> result)
        {
            try
            {
                if (wave == null || result == null) return;
                PmcTrySpawnPatch.SawZones(wave, result);
                if (!wave.BossType.IsPmcBot()) return;
                var c = RaidContext.Current;
                if (c != null) PmcZones.Reorder(c, result);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("PmcZoneOrder", ex);
            }
        }
    }
}
