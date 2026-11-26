using System;
using System.Collections.Generic;
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
    /// BossEscortAmount → EscortCount(Supports가 있으면 그 항목 인원을 더한다), BossZone → BornZone, BossChance → ShallSpawn.
    /// Time은 지금 레이드 경과 초(0 이상, 시작 웨이브 표시 Time&lt;0이 아님: BossLocationSpawn.IsStartWave :108).
    /// </summary>
    internal static class WaveFactory
    {
        /// <summary>
        /// 스캐브(assault) 1마리 웨이브. zoneName 구역 안의 실제 지점은 바닐라(SelectAISpawnPoints)가 고른다.
        /// playerScav는 SpawnWave.IsPlayers에 넣기만 한다: 4.1의 BotSpawner.ActivateBotsByWave(SpawnWave)(BotSpawner.cs:383-421)는
        /// IsPlayers를 읽지 않으므로 효과가 없다. 그래서 0.2에서는 scav.pscavChance를 적용하지 않고 ScavDirector는 늘 false를 넘긴다
        /// (웹 띠 "다음 판", 컨트롤러 결정 14).
        /// </summary>
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

        /// <summary>
        /// 보충 PMC 분대. zoneName ""이면 Init이 BornZone ""으로 두고 TrySpawn이 AvailableZonesPmc를 섞어 고른다(Task 5 패치 경로).
        /// 분대장은 g.Difficulty. g.Escorts가 있으면(대원마다 난이도) 대원은 난이도별 Supports 항목으로 나가고 BossEscortAmount는 0,
        /// 없으면 기본 호위(BossEscortAmount = 크기 − 1)가 분대장 난이도를 따른다.
        /// </summary>
        public static BossLocationSpawn Pmc(RefillGroup g, string zoneName)
        {
            var role = g.Usec ? "pmcUSEC" : "pmcBEAR";
            return Build(role, Math.Max(1, g.Size), Diff(g.Difficulty), zoneName, null, Supports(g));
        }

        /// <summary>1.1.0 '지금 넣어 보기': 역할·대원 역할·대원 수 그대로. 모르는 역할이면 null. at이 있으면 그 위치 근처 구역부터.</summary>
        public static BossLocationSpawn? Custom(string role, string escort, int escorts, string diff, Vector3? at)
        {
            if (!Enum.TryParse<WildSpawnType>(role, true, out _) || !Enum.TryParse<WildSpawnType>(escort, true, out _)) return null;
            var w = Build(role, 1 + Math.Max(0, escorts), Diff(diff), "", at);
            w.BossEscortType = escort;
            w.Init();
            return w;
        }

        /// <summary>정해진 위치 근처 분대(F8용). PerfectPos를 주면 GetPossibleZones가 그 위치에 가까운 구역 순으로 고른다.</summary>
        public static BossLocationSpawn AtPoint(WildSpawnType role, int count, Vec3 pos, string zoneName, string diff = "normal") =>
            Build(role.ToString(), Math.Max(1, count), string.IsNullOrEmpty(diff) ? "normal" : diff, zoneName, new Vector3(pos.X, pos.Y, pos.Z));

        private static BossLocationSpawn Build(string role, int size, string diff, string zoneName, Vector3? perfectPos, WildSpawnSupports[]? supports = null)
        {
            var w = new BossLocationSpawn
            {
                BossName = role,
                BossEscortType = role,
                BossChance = 100f,
                BossDifficult = diff,
                BossEscortDifficult = diff,
                // Supports가 대원을 다 맡으면 기본 호위 수는 0(Init이 둘을 더해 EscortCount를 만든다).
                BossEscortAmount = supports == null ? (size - 1).ToString() : "0",
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
                Supports = supports,
            };
            w.Init();
            return w;
        }

        /// <summary>
        /// 대원마다 난이도(RefillGroup.Escorts): 대원을 난이도별로 묶어 Supports 항목(대원 역할·난이도 하나·인원)으로 만든다.
        /// 게임은 항목마다 BotCreationData 하나를 같은 spawnParams로 만들어 한 분대로 묶는다(BotBossSpawn.SpawnEscort).
        /// 대원 난이도가 없으면(꺼짐·1인·옛 서버) null = 기본 BossEscortAmount 경로.
        /// </summary>
        private static WildSpawnSupports[]? Supports(RefillGroup g)
        {
            if (g.Escorts == null) return null;
            int escorts = Math.Min(g.Escorts.Length, Math.Max(1, g.Size) - 1);
            if (escorts <= 0) return null;
            var type = g.Usec ? WildSpawnType.pmcUSEC : WildSpawnType.pmcBEAR;
            var order = new List<string>();
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < escorts; i++)
            {
                var d = Diff(g.Escorts[i]);
                if (!counts.ContainsKey(d)) { order.Add(d); counts[d] = 0; }
                counts[d]++;
            }
            var supports = new WildSpawnSupports[order.Count];
            for (int i = 0; i < supports.Length; i++)
                supports[i] = new WildSpawnSupports { BossEscortType = type, BossEscortAmount = counts[order[i]], BossEscortDifficult = new[] { order[i] } };
            return supports;
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
