using System;
using EFT;
using Gravity.Shared;
using UnityEngine;

namespace Gravity.Client.Directors
{
    /// <summary>
    /// 클라에서 바로 넘기는 웨이브 객체를 만든다(ActivateBotsByWave용).
    /// SpawnWave(SpawnWave.cs)는 BotSpawner.ActivateBotsByWave(SpawnWave)(BotSpawner.cs:383-421)가 BotsCount·Side·WildSpawnType·Difficulty·Time·
    /// KeepZoneOnSpawn·ChanceGroup·SpawnAreaName·WithCheckMinMax만 읽는다. SpawnAreaName은 BotZone.name(= NameZone, BotZone.cs:102)과 비교한다(:403).
    /// BossLocationSpawn은 서버 JSON과 같은 문자열 필드를 채운 뒤 BossSpawnScenario.Init(BossSpawnScenario.cs:128)처럼
    /// BossLocationSpawn.Init()(BossLocationSpawn.cs:161)을 부른다: BossName → BossType, 난이도 문자열 → BossDif·EscortDif,
    /// BossEscortAmount → EscortCount, BossZone → BornZone, BossChance → ShallSpawn.
    /// Time은 지금 레이드 경과 초(0 이상, 시작 웨이브 표시 Time&lt;0이 아님: BossLocationSpawn.IsStartWave :108).
    /// </summary>
    internal static class WaveFactory
    {
        /// <summary>스캐브(assault) 1마리 웨이브. zoneName 구역 안의 실제 지점은 바닐라(SelectAISpawnPoints)가 고른다.</summary>
        public static SpawnWave Scav(string zoneName, BotDifficulty d, bool playerScav) => new SpawnWave
        {
            BotsCount = 1,
            Side = EPlayerSide.Savage,
            WildSpawnType = WildSpawnType.assault,
            Difficulty = d,
            Time = Now(),
            SpawnAreaName = zoneName ?? "",
            IsPlayers = playerScav,
            ChanceGroup = 0f,
            WithCheckMinMax = true,
            KeepZoneOnSpawn = false,
        };

        /// <summary>보충 PMC 분대. zoneName ""이면 Init이 BornZone ""으로 두고 TrySpawn이 AvailableZonesPmc를 섞어 고른다(Task 5 패치 경로).</summary>
        public static BossLocationSpawn Pmc(RefillGroup g, string zoneName)
        {
            var role = g.Usec ? "pmcUSEC" : "pmcBEAR";
            return Build(role, Math.Max(1, g.Size), Diff(g.Difficulty), zoneName, null);
        }

        /// <summary>정해진 위치 근처 분대(F8용). PerfectPos를 주면 GetPossibleZones가 그 위치에 가까운 구역 순으로 고른다.</summary>
        public static BossLocationSpawn AtPoint(WildSpawnType role, int count, Vec3 pos, string zoneName) =>
            Build(role.ToString(), Math.Max(1, count), "normal", zoneName, new Vector3(pos.X, pos.Y, pos.Z));

        private static BossLocationSpawn Build(string role, int size, string diff, string zoneName, Vector3? perfectPos)
        {
            var w = new BossLocationSpawn
            {
                BossName = role,
                BossEscortType = role,
                BossChance = 100f,
                BossDifficult = diff,
                BossEscortDifficult = diff,
                BossEscortAmount = (size - 1).ToString(),
                BossZone = zoneName ?? "",
                BossPlayer = false,
                Time = Now(),
                Delay = 0f,
                TriggerId = "",
                TriggerName = "",
                IgnoreMaxBots = false,
                ForceSpawn = false,
                DependKarma = false,
                PerfectPos = perfectPos,
                Supports = null,
            };
            w.Init();
            return w;
        }

        /// <summary>서버 난이도 문자열(easy·normal·hard·impossible). 모르는 값이면 normal(Init의 Enum.Parse가 던지지 않게).</summary>
        private static string Diff(string? s) =>
            !string.IsNullOrEmpty(s) && Enum.TryParse<BotDifficulty>(s, true, out var d) ? d.ToString() : "normal";

        private static float Now()
        {
            var c = RaidContext.Current;
            return c == null ? 0f : Math.Max(0f, c.Elapsed);
        }
    }
}
