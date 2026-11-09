using System;
using System.Collections.Generic;

namespace Gravity.Shared
{
    /// <summary>
    /// 봇 역할 → 상한 표 종류(1.0, 사용자 11-10). 서버(맵 항목의 상한 무시 표시)와 클라(맵 상한 계산·종류별 최대 인원)가 같은 규칙을 쓴다.
    /// pmc·scav는 Gravity가 직접 관리하고, boss·special·faction·event는 상한 표의 방식을 따른다. 나머지(BTR 사수·모르는 모드 역할)는 other = 바닐라.
    /// </summary>
    public static class CapKinds
    {
        public const string Pmc = "pmc", Scav = "scav", Boss = "boss", Special = "special", Faction = "faction", Event = "event", Other = "other";
        public const string Separate = "separate", Vanilla = "vanilla", Respect = "respect";

        /// <summary>상한 표에서 방식을 고르는 종류(웹 줄 순서).</summary>
        public static readonly string[] Table = { Boss, Special, Faction, Event };

        private static readonly HashSet<string> Scavs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "assault", "marksman", "assaultGroup", "cursedAssault" };

        private static readonly HashSet<string> Specials = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "pmcBot", "exUsec", "sectantPriest", "sectantWarrior", "arenaFighterEvent", "arenaFighter" };

        private static readonly HashSet<string> Events = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gifter", "peacemaker", "crazyAssaultEvent", "skier", "spiritWinter", "spiritSpring", "peacefullZryachiyEvent",
            "ravangeZryachiyEvent", "sectactPriestEvent", "sectantPredvestnik", "sectantPrizrak", "sectantOni"
        };

        private static readonly HashSet<string> BossExtra = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tagillaHelperAgro" };

        /// <param name="role">WildSpawnType 이름(모드 역할은 번호 문자열일 수 있다).</param>
        /// <param name="factionRoles">팩션으로 셀 역할(RUAF·블랙 디비전 등, 이름 또는 번호). 먼저 본다.</param>
        public static string Of(string? role, ICollection<string>? factionRoles = null)
        {
            if (string.IsNullOrEmpty(role)) return Other;
            if (factionRoles != null && factionRoles.Contains(role!)) return Faction;
            if (role!.Equals("pmcUSEC", StringComparison.OrdinalIgnoreCase) || role.Equals("pmcBEAR", StringComparison.OrdinalIgnoreCase)) return Pmc;
            if (Scavs.Contains(role)) return Scav;
            if (Specials.Contains(role)) return Special;
            if (Events.Contains(role) || role.StartsWith("infected", StringComparison.OrdinalIgnoreCase)) return Event;
            if (role.StartsWith("boss", StringComparison.OrdinalIgnoreCase) || role.StartsWith("follower", StringComparison.OrdinalIgnoreCase)
                || BossExtra.Contains(role)) return Boss;
            return Other;
        }

        /// <summary>방식이 상한 계산에서 빼는 것인지.</summary>
        public static bool Excluded(CapTableDto? t, string kind) =>
            t != null && t.Kinds != null && t.Kinds.TryGetValue(kind, out var k) && k != null && k.Mode == Separate;

        /// <summary>종류별 최대 인원(0 = 없음).</summary>
        public static int MaxOf(CapTableDto? t, string kind) =>
            t != null && t.Kinds != null && t.Kinds.TryGetValue(kind, out var k) && k != null ? Math.Max(0, k.Max) : 0;

        /// <summary>
        /// 무리(분대장 + 호위 escorts명)가 이 종류 최대 인원 안에 들어가도록 줄인 호위 수. -1이면 이미 꽉 차서 무리 전체를 건너뛴다.
        /// 최대 인원이 없으면 그대로.
        /// </summary>
        public static int FitEscorts(int max, int alive, int escorts)
        {
            if (max <= 0) return escorts;
            int room = max - Math.Max(0, alive);
            if (room <= 0) return -1;
            return Math.Max(0, Math.Min(escorts, room - 1));
        }

        /// <summary>
        /// PMC 예약 자리 중 아직 PMC가 안 채운 몫: 스캐브·근처 보충은 이만큼 덜 쓴다.
        /// </summary>
        public static int ReserveLeft(int reserve, int pmcAliveOrHeld) => Math.Max(0, reserve - Math.Max(0, pmcAliveOrHeld));
    }
}
