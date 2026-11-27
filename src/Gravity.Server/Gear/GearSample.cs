using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Bot;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Servers;

namespace Gravity.Gear;

/// <summary>예시 봇 한 명 요약(아이템 id는 웹이 카탈로그로 이름을 붙인다).</summary>
public sealed record SampleBot(int Level, string? Weapon, string? Scope, string? Armor, int ArmorClass, string? Helmet, int HelmetClass, string? Rig);

/// <summary>
/// 1.1.6 티어별 예시 봇: 서버 안에서 SPT 봇 생성(BotController.Generate, 하네스가 HTTP로 부르는 것과 같은 함수)으로
/// 지금 저장된 설정 그대로 봇을 몇 명 뽑아 주무기·조준경·방탄복·헬멧·리그를 돌려준다. 티어는 PreviewTier로 고정하고,
/// 미리보기 봇은 이번 판 티어 통계·입힌 수에 넣지 않는다.
/// </summary>
public static class GearSample
{
    /// <summary>미리보기 생성 중이면 고정할 티어(0~6). 비동기 호출 사슬을 따라간다.</summary>
    public static readonly AsyncLocal<int?> PreviewTier = new();
    public static IServiceProvider? Services { get; set; }

    /// <summary>웹 역할 키 → SPT 봇 종류.</summary>
    public static string? RoleOf(string key) => key switch
    {
        "usec" => "pmcUSEC", "bear" => "pmcBEAR", "scav" => "assault", "raider" => "pmcBot", "rogue" => "exUsec", _ => null
    };

    public static SampleBot Summarize(BotBase bot, GearItems items)
    {
        var all = bot.Inventory?.Items?.ToList() ?? [];
        ItemInfo? Info(Item i) => items.Info.GetValueOrDefault(i.Template.ToString());
        Item? Slot(string s) => all.FirstOrDefault(i => i.SlotId == s);
        IEnumerable<Item> Below(Item root)
        {
            var ids = new HashSet<string> { root.Id.ToString() };
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (var i in all)
                    if (i.ParentId is { } p && ids.Contains(p) && ids.Add(i.Id.ToString())) { grew = true; yield return i; }
            }
        }
        int Class(Item? root) => root == null ? 0 : Below(root).Prepend(root).Select(i => Info(i)?.ArmorClass ?? 0).DefaultIfEmpty(0).Max();

        var weapon = Slot("FirstPrimaryWeapon");
        var scope = weapon == null ? null : Below(weapon).FirstOrDefault(i => Info(i) is { Section: "parts", Slot: "scope" });
        var rig = Slot("TacticalVest");
        var armor = Slot("ArmorVest") ?? (rig != null && Class(rig) > 0 ? rig : null);
        var helmet = Slot("Headwear");
        return new SampleBot(bot.Info?.Level ?? 0, weapon?.Template.ToString(), scope?.Template.ToString(),
            armor?.Template.ToString(), Class(armor), helmet?.Template.ToString(), Class(helmet), rig?.Template.ToString());
    }

    /// <summary>`POST api/gear/sample` {role, tier(1~7), n(1~5)}.</summary>
    public static async Task<(int Status, object Body)> Run(string role, int tier, int n)
    {
        if (GearRuntime.Active() == null) return (200, new { off = true });
        var spt = RoleOf(role);
        if (spt == null || tier is < 1 or > 7) return (400, new { error = "role·tier 형식 오류 / Bad role or tier" });
        n = Math.Clamp(n, 1, 5);
        if (Services?.GetService(typeof(BotController)) is not BotController ctl || Services.GetService(typeof(SaveServer)) is not SaveServer save)
            return (503, new { error = "봇 생성기를 찾지 못했습니다 / Bot generator not available" });
        // 봇 레벨 기준이 되는 프로필(레벨만 읽음). 레벨이 있는 첫 프로필.
        MongoId? sid = save.GetProfiles().Where(kv => kv.Value?.CharacterData?.PmcData?.Info?.Level is > 0).Select(kv => (MongoId?)kv.Key).FirstOrDefault();
        if (sid == null) return (503, new { error = "캐릭터가 있는 프로필이 없습니다 / No profile with a character" });
        PreviewTier.Value = tier - 1;
        try
        {
            var bots = await ctl.Generate(sid.Value, new GenerateBotsRequestData
            {
                Conditions = [new GenerateCondition { Role = spt, Limit = n, Difficulty = "normal" }]
            });
            return (200, new { bots = bots.Take(n).Select(b => Summarize(b, GearRuntime.Items)).ToList() });
        }
        catch (Exception ex) { return (500, new { error = ex.Message }); }
        finally { PreviewTier.Value = null; }
    }
}
