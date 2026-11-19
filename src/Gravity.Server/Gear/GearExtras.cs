using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Gravity.Gear;

/// <summary>
/// 1.1.0 봇별 장비·티어 화면에만 있던 칸의 실제 동작: 상탄/하탄, 상탄 따로 넣기, 귀중품·열쇠/카드, 의료품 남은 양.
/// SPT 객체에 손대는 부분은 패치가 부르고, 고르는 규칙은 시험할 수 있게 여기 둔다.
/// </summary>
public static class GearExtras
{
    // SPT 부모 클래스: 귀중품(Jewelry)·정보(Info)·기계 열쇠·키카드
    public const string Jewelry = "57864a3d24597754843f8721", InfoItem = "5448ecbe4bdc2d60728b4568",
        MechanicalKey = "5c99f98d86f7745c314214b3", Keycard = "5c164d2286f774194c5e69fa";

    /// <summary>같은 구경 탄 후보(막히지 않음·관통 상한 이하·티어 이하), 관통 낮은 순.</summary>
    public static List<(string Id, double Pen)> AmmoOptions(GearContext c, string caliber, double penCap)
    {
        var o = new List<(string, double)>();
        foreach (var (id, info) in c.Items.Info)
        {
            if (info.Section != "ammo" || info.Slot != caliber || info.Pen > penCap || c.Blocked(id)) continue;
            if (c.TierOf("ammo/" + caliber, id) > c.Pick.Tier && !c.Settings.ItemTier.ContainsKey(id)) continue;
            o.Add((id, info.Pen));
        }
        return o.OrderBy(x => x.Item2).ThenBy(x => x.Item1, StringComparer.Ordinal).ToList();
    }

    /// <summary>예비 탄: 지금 탄이 예비 탄 상한 이하면 그대로, 넘으면 상한 안에서 관통이 가장 높은 탄(없으면 null = 그대로).</summary>
    public static string? SpareAmmo(GearContext c, string current)
    {
        if (!c.Settings.SplitAmmo || c.Info(current) is not { Section: "ammo" } info) return null;
        double cap = c.Caps.Spare;
        if (info.Pen <= cap) return null;
        var opts = AmmoOptions(c, info.Slot, cap);
        return opts.Count == 0 ? null : opts[^1].Id;
    }

    /// <summary>상탄: 지금 탄보다 관통이 높은 같은 구경 탄 중 가장 좋은 것(장전 탄 상한 안). 없으면 null.</summary>
    public static string? TopAmmo(GearContext c, string current)
    {
        if (c.Info(current) is not { Section: "ammo" } info) return null;
        var opts = AmmoOptions(c, info.Slot, c.Caps.Pen);
        return opts.Count > 0 && opts[^1].Pen > info.Pen ? opts[^1].Id : null;
    }

    /// <summary>탄창 한 묶음을 아래(원래 탄)·위(상탄)로 나눌 발 수. 2발 미만이면 0.</summary>
    public static int TopCount(int rounds, double pct) =>
        rounds < 2 ? 0 : Math.Clamp((int)Math.Round(rounds * pct / 100.0), 1, rounds - 1);

    /// <summary>
    /// 봇 인벤토리의 탄창(cartridges 칸)마다 위쪽 일부를 상탄으로 바꾼다. 위쪽 = 같은 탄창 안 마지막 묶음 다음 칸(location이 큰 쪽, 먼저 나감).
    /// 바꾼 탄창 수를 돌려준다.
    /// </summary>
    public static int TopLoad(List<Item> items, GearContext c, Random rnd)
    {
        var role = c.Role;
        if (!role.TopOn || rnd.NextDouble() * 100 >= role.TopChance) return 0;
        int done = 0;
        foreach (var mag in items.Where(i => i.SlotId != null).GroupBy(i => i.ParentId).ToList())
        {
            var carts = mag.Where(i => i.SlotId == "cartridges").ToList();
            if (carts.Count != 1 || mag.Key == null) continue;   // 이미 여러 묶음이면 건드리지 않음
            var bottom = carts[0];
            int n = (int)(bottom.Upd?.StackObjectsCount ?? 0);
            int top = TopCount(n, role.TopPct);
            if (top == 0 || TopAmmo(c, bottom.Template.ToString()) is not { } better) continue;
            bottom.Upd!.StackObjectsCount = n - top;
            bottom.Location = 0;
            items.Add(new Item { Id = new MongoId(), Template = better, ParentId = mag.Key, SlotId = "cartridges", Location = 1, Upd = new Upd { StackObjectsCount = top } });
            done++;
        }
        return done;
    }

    /// <summary>가격 순 7등분 중 봇 티어 이하 칸에서 하나(후보 없으면 null).</summary>
    public static string? PickByBand(IReadOnlyList<(string Id, double Price)> pool, int tier, Random rnd)
    {
        if (pool.Count == 0) return null;
        var sorted = pool.OrderBy(x => x.Price).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        int upto = Math.Max(1, (int)Math.Ceiling(sorted.Count * (Math.Clamp(tier, 0, 6) + 1) / 7.0));
        return sorted[rnd.Next(0, upto)].Id;
    }

    /// <summary>귀중품·열쇠/카드 후보: 부모 클래스로 고른 뒤 막힌 것(블랙리스트·퀘스트·꺼진 모드)을 뺀다.</summary>
    public static List<(string Id, double Price)> Pool(GearContext c, IEnumerable<string> ids) =>
        ids.Where(id => !c.Blocked(id)).Select(id => (id, c.Items.Info.TryGetValue(id, out var i) ? i.Price : 0)).ToList();

    /// <summary>의료품·음식 남은 양 무작위를 끈 봇: 가득 채운다.</summary>
    public static int FillConsumables(IEnumerable<Item> items, Func<MongoId, TemplateItem?> tpl)
    {
        int n = 0;
        foreach (var it in items)
        {
            if (it.Upd == null || tpl(it.Template) is not { Properties: { } p }) continue;
            if (it.Upd.MedKit != null && p.MaxHpResource is > 0) { it.Upd.MedKit.HpResource = p.MaxHpResource.Value; n++; }
            if (it.Upd.FoodDrink != null && p.MaxResource is > 0) { it.Upd.FoodDrink.HpPercent = p.MaxResource.Value; n++; }
        }
        return n;
    }
}
