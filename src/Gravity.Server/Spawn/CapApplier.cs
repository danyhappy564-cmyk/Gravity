using Gravity.Core;
using Gravity.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace Gravity.Spawn;

/// <summary>
/// 상한 표(1.0, 사용자 11-10)를 레이드용 맵 복제본의 보스 항목(BossLocationSpawn)에 쓴다. 다른 고침이 다 끝난 뒤 마지막에 한 번.
/// - separate(따로 셈): 상한을 무시하고 나온다(IgnoreMaxBots=true). 클라가 맵 상한 계산에서 이 종류를 뺀다.
/// - respect(상한 지킴): IgnoreMaxBots=false. 자리가 나면 나온다(게임 대기열).
/// - vanilla(원본대로): 손대지 않는다.
/// PMC(Gravity 계획 분대·다른 모드 PMC)와 스캐브, 모르는 역할(other)은 건드리지 않는다.
/// </summary>
public static class CapApplier
{
    public static int Apply(LocationBase loc, CapTable t, ICollection<string>? factionRoles)
    {
        if (loc.BossLocationSpawn == null) return 0;
        int changed = 0;
        var list = loc.BossLocationSpawn.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            var kind = CapKinds.Of(e.BossName, factionRoles);
            if (Array.IndexOf(CapKinds.Table, kind) < 0) continue;
            var mode = t.For(kind).Mode;
            bool? want = WaveMark.IsExtraSpecial(e.TriggerId, e.TriggerName) ? false
                : mode == CapKinds.Separate ? true : mode == CapKinds.Respect ? false : null;
            if (want == null || e.IgnoreMaxBots == want) continue;
            list[i] = e with { IgnoreMaxBots = want.Value };
            changed++;
        }
        if (changed > 0) loc.BossLocationSpawn = list;
        return changed;
    }
}
