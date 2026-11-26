using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Gravity.Gear;

/// <summary>입는 장비끼리의 제한(막는 칸·충돌 목록) 한 아이템분.</summary>
public sealed record WearRule(bool BlocksEarpiece, bool BlocksEyewear, bool BlocksFaceCover, bool BlocksArmorVest, IReadOnlySet<MongoId> Conflicts, bool BlocksHeadwear = false)
{
    public static WearRule? Of(TemplateItemProperties? p)
    {
        if (p == null) return null;
        var c = p.ConflictingItems;
        bool any = p.BlocksEarpiece == true || p.BlocksEyewear == true || p.BlocksFaceCover == true || p.BlocksArmorVest == true
                   || p.BlocksHeadwear == true || c is { Count: > 0 };
        return any ? new WearRule(p.BlocksEarpiece == true, p.BlocksEyewear == true, p.BlocksFaceCover == true, p.BlocksArmorVest == true,
            c is { Count: > 0 } ? new HashSet<MongoId>(c) : [], p.BlocksHeadwear == true) : null;
    }

    /// <summary>둘 중 하나라도 막거나 충돌하면 막는다.</summary>
    public static WearRule? Merge(WearRule? a, WearRule? b) => a == null ? b : b == null ? a :
        new WearRule(a.BlocksEarpiece || b.BlocksEarpiece, a.BlocksEyewear || b.BlocksEyewear, a.BlocksFaceCover || b.BlocksFaceCover,
            a.BlocksArmorVest || b.BlocksArmorVest, a.Conflicts.Count == 0 ? b.Conflicts : b.Conflicts.Count == 0 ? a.Conflicts : a.Conflicts.Union(b.Conflicts).ToHashSet(),
            a.BlocksHeadwear || b.BlocksHeadwear);

    public bool Blocks(string slot) => slot switch
    {
        "Earpiece" => BlocksEarpiece, "Eyewear" => BlocksEyewear, "FaceCover" => BlocksFaceCover, "ArmorVest" => BlocksArmorVest, "Headwear" => BlocksHeadwear, _ => false
    };
}

/// <summary>
/// 1.1.5(INBOX #93): LessRestrictingHeadwear·SVM(EquipRigsWithArmors) 같은 모드는 플레이어를 위해 공용 아이템 표의 머리 장비·방탄복 제한을
/// 지우는데, 봇 생성도 같은 표를 써서 봇이 헬멧+헤드셋 겹침 같은 어색한 조합을 입는다. 그 모드들이 고치기 전(PostLoad) 제한을 떠 두고,
/// 봇에는 '원래 제한 ∪ 지금 제한'으로 겹친 장비를 뺀다(<see cref="GearExtras.DropHeadClashes"/>). 플레이어 쪽은 건드리지 않는다.
/// </summary>
public static class WearRules
{
    public static IReadOnlyDictionary<MongoId, WearRule> Original { get; private set; } = new Dictionary<MongoId, WearRule>();

    public static int Snapshot(IReadOnlyDictionary<MongoId, TemplateItem> items)
    {
        var d = new Dictionary<MongoId, WearRule>();
        foreach (var (id, t) in items)
            if (WearRule.Of(t.Properties) is { } r) d[id] = r;
        Original = d;
        return d.Count;
    }

    public static WearRule? For(MongoId id, Func<MongoId, TemplateItem?> tpl) => WearRule.Merge(Original.GetValueOrDefault(id), WearRule.Of(tpl(id)?.Properties));
}

/// <summary>LessRestrictingHeadwear(PostLoad+1)보다 먼저 원래 제한을 뜬다.</summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class WearRulesSnapshot(TemplateTable templates) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try { WearRules.Snapshot(templates.Items); } catch { /* 없으면 지금 표만 쓴다 */ }
        return Task.CompletedTask;
    }
}
