using System;

namespace Gravity.Shared
{
    /// <summary>옛 서버의 bool과 새 방식·상한을 함께 해석하는 탈출 보충 규칙이다.</summary>
    public static class ExtractRefillRule
    {
        public static string Mode(ClientRaidSettings s) => s.ExtractRefillMode ?? (s.RefillAfterExtract ? "infinite" : "off");
        public static int Cap(ClientRaidSettings s) => Math.Max(0, Math.Min(100, s.ExtractRefillCap ?? 20));
        public static bool CanQueue(ClientRaidSettings s, bool pmc, int queued, float elapsed) =>
            s.Managed && pmc && queued < Cap(s) && !SpawnExpiry.PastHardStop(elapsed, s.HardStopPmcSec)
            && (s.MapId != "labyrinth" || s.AllowPmcLabyrinth)
            && (Mode(s) == "all" || (Mode(s) == "infinite" && s.Inf != null));
    }
}
