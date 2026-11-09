using System.Text.Json.Nodes;
using Gravity.Core;

namespace Gravity.Gear;

/// <summary>저장된 칸별 무게. 칸 없음=자동, 빈 칸/0=제외. 옛 근거리·원거리 표도 읽는다.</summary>
public static class GearPools
{
    internal static IReadOnlyDictionary<string,IReadOnlyDictionary<string,double>> Read(ConfigModel.Reader r, string path)
    {
        var pools = new Dictionary<string,IReadOnlyDictionary<string,double>>();
        foreach (var sec in new[]{"weapons","secondaryWeapons","armor","ammo","meds","parts"})
            if (r.Node(path + sec) is JsonObject slots)
                foreach (var (slot, node) in slots)
                    if (node is JsonObject weights)
                    {
                        var clean = new Dictionary<string,double>();
                        foreach (var (id, _) in weights)
                            if (id.Length == 24 && id.All(char.IsAsciiHexDigit))
                                clean[id.ToLowerInvariant()] = r.Num(path + sec + "." + slot + "." + id, 0, 0, 10000);
                        // 유효한 아이템 ID가 하나도 없는 칸(옛 웹 예시 아이템만 저장된 칸)은 '자동'으로 본다 — 빈 표로 읽으면 그 칸 아이템이 전부 빠진다.
                        if (clean.Count > 0) pools[sec + "/" + slot] = clean;
                    }
        return pools;
    }

    internal static IReadOnlyDictionary<string,double> Chances(ConfigModel.Reader r,string path) => r.Node(path) is JsonObject obj
        ? obj.Where(kv=>kv.Key.Length <= 80).ToDictionary(kv=>kv.Key,kv=>r.Num(path+"."+kv.Key,0,0,100)) : new Dictionary<string,double>();

    internal static IReadOnlyDictionary<string,IReadOnlyDictionary<int,double>> Generation(ConfigModel.Reader r,string path)
    {
        var result=new Dictionary<string,IReadOnlyDictionary<int,double>>();
        if(r.Node(path) is not JsonObject all)return result;
        foreach(var (key,node) in all)
            if(node is JsonObject weights)
            {
                var row=new Dictionary<int,double>();
                foreach(var (n,_) in weights)
                    if(int.TryParse(n,out var count) && count is >=0 and <=30)row[count]=r.Num(path+"."+key+"."+n,0,0,10000);
                if(row.Values.Any(w=>w>0))result[key]=row;
            }
        return result;
    }

    public static double? Weight(GearTable table, ItemInfo info, string id, string? equipmentSlot = null)
    {
        if (equipmentSlot == "SecondPrimaryWeapon" && table.Pools.TryGetValue("secondaryWeapons/"+info.Slot,out var secondary)) return secondary.GetValueOrDefault(id);
        if (table.Pools.TryGetValue(info.Section + "/" + info.Slot, out var pool)) return pool.GetValueOrDefault(id);
        if (info.Section == "weapons" && info.Slot is not ("holster" or "scabbard"))
        {
            // 구버전은 근거리/원거리 모두 주무기였다. 중복 ID는 큰 무게 하나로 합친다.
            var legacy = new[]{"weapons/primaryShort","weapons/primaryLong"}.Where(table.Pools.ContainsKey).ToArray();
            if (legacy.Length > 0) return legacy.Max(k=>table.Pools[k].GetValueOrDefault(id));
        }
        // 목록 전용 표: 장비 칸(무기·방어구)에서 표에 없는 아이템은 무게 0. 부품·방탄판·탄약은 기존 규칙 그대로.
        if (table.Only && equipmentSlot != null && info.Section is "weapons" or "armor") return 0;
        return null;
    }

    public static bool Controls(GearTable table, string slot, GearItems items) =>
        table.Pools.Keys.Any(k=>items.PoolEquipment.TryGetValue(k.StartsWith("secondaryWeapons/")?"weapons/"+k[17..]:k,out var slots)&&slots.Contains(slot)&&(!k.StartsWith("secondaryWeapons/")||slot=="SecondPrimaryWeapon"))
        || slot is "FirstPrimaryWeapon" or "SecondPrimaryWeapon" && (table.Pools.ContainsKey("weapons/primaryShort") || table.Pools.ContainsKey("weapons/primaryLong"));

    /// <summary>명시한 아이템만 순회한다(이 티어 표의 유한한 ID 수가 최대 횟수). 전체 DB를 봇마다 검색하지 않는다.</summary>
    public static IEnumerable<string> Seeds(GearTable table,GearItems items,string slot)
    {
        foreach(var(key,pool) in table.Pools)
        {
            if(key.StartsWith("secondaryWeapons/")&&slot!="SecondPrimaryWeapon")continue;
            foreach(var(id,w) in pool)
                if(w>0&&items.Info.TryGetValue(id,out var info)&&info.Available&&info.EquipmentSlots.Contains(slot)&&Weight(table,info,id,slot)>0)yield return id;
        }
    }
}
