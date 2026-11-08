using System.Collections.Generic;

namespace Gravity.Shared
{
    public enum CapVerdict { Allow, Wait }

    /// <summary>"동시에 살아 있는 그룹" 제한. 규칙에 걸리는 새 그룹은 Wait.</summary>
    public static class AliveGroupRule
    {
        public static CapVerdict Check(IReadOnlyList<int> aliveGroupSizes, int newSize, GroupCapDto cap)
        {
            if (cap == null || !cap.On || cap.Scope == "raid" || cap.Rules == null) return CapVerdict.Allow;
            for (int r = 0; r < cap.Rules.Count; r++)
            {
                var rule = cap.Rules[r];
                if (!Matches(rule, newSize)) continue;
                int alive = 0;
                for (int i = 0; i < aliveGroupSizes.Count; i++)
                    if (Matches(rule, aliveGroupSizes[i])) alive++;
                if (alive >= rule.Count) return CapVerdict.Wait;
            }
            return CapVerdict.Allow;
        }

        private static bool Matches(GroupCapRuleDto rule, int size) =>
            rule.Cmp == "gte" ? size >= rule.Size : size == rule.Size;
    }
}
