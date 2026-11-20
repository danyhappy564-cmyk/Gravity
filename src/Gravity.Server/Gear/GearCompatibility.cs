using SPTarkov.Server.Core.Models.Common;

namespace Gravity.Gear;

/// <summary>새로 고른 장비의 부품 목록을 봇 복제본에만 보충한다. DB와 캐시의 배열은 수정하지 않는다.</summary>
public static class GearCompatibility
{
    public const int MaxParents = 512;
    public const int MaxDepth = 12;

    public static int AddMissing(Dictionary<MongoId,Dictionary<string,HashSet<MongoId>>> mods, IEnumerable<string> roots, GearItems items)
    {
        var pending=new Queue<(string Id,int Depth,string[] Ancestors)>();
        foreach(var id in roots.Distinct().Take(MaxParents))pending.Enqueue((id,0,[]));
        var seen=new HashSet<string>();
        int added=0;
        while(pending.Count>0&&seen.Count<MaxParents)
        {
            var(id,depth,ancestors)=pending.Dequeue();
            items.DonorMods.TryGetValue(id,out var donor);
            items.CompatibleMods.TryGetValue(id,out var compat);
            if(!seen.Add(id)||depth>=MaxDepth||(donor==null&&compat==null))continue;
            // 1.0.9: 봇 원본 표에 이 부모가 있으면 그 표의 칸만 쓴다(맞는 부품 전부 대신). 빌린 표에 없는 필수 칸만 호환 목록으로 채운다.
            var slots=donor==null?compat!:WithRequired(donor,compat,items.Info.GetValueOrDefault(id)?.RequiredSlots);
            bool exists=mods.TryGetValue(id,out var current);
            if(!exists){current=new();mods[id]=current;added++;}
            foreach(var(slot,candidates) in slots)
            {
                // 원래 봇의 호환 규칙·다른 모드가 만든 부품 칸은 그대로 둔다.
                if(!current!.ContainsKey(slot))current[slot]=candidates.Where(x=>x!=id&&!ancestors.Contains(x)).Select(x=>(MongoId)x).ToHashSet();
                // 큐 길이도 제한해 잘못된 모드 데이터로 후보가 폭증하지 않게 한다.
                if(depth+1<MaxDepth)
                    foreach(var child in current[slot])
                    {
                        if(pending.Count>=MaxParents)break;
                        if(!seen.Contains(child.ToString()))pending.Enqueue((child.ToString(),depth+1,[..ancestors,id]));
                    }
            }
        }
        return added;
    }

    /// <summary>
    /// 1.1.0(#64·#68): 봇 표 안의 부품 후보인데 그 부품의 칸 표가 없는 것(구멍)을 다른 봇 표(DonorMods)로만 메운다. 호환 목록 전부는 쓰지 않는다.
    /// SPT 원본 USEC·BEAR 표의 Sako TRG M10은 수신기에 조준경 마운트가 있는데 그 마운트 표가 없어 조준경이 한 번도 안 달렸다
    /// (버드아이 표에는 마운트 → 링 마운트 → 조준경이 있음). 이미 있는 표는 건드리지 않는다.
    /// </summary>
    public static int FillHoles(Dictionary<MongoId,Dictionary<string,HashSet<MongoId>>> mods, GearItems items, Func<string,bool>? blocked = null)
    {
        if(items.DonorMods.Count==0)return 0;
        int added=0;
        var scan=mods.Values.ToList();
        for(int round=0;round<MaxDepth&&scan.Count>0&&added<MaxParents;round++)
        {
            var next=new List<Dictionary<string,HashSet<MongoId>>>();
            foreach(var slots in scan)
                foreach(var set in slots.Values)
                {
                    if(set==null)continue;
                    foreach(var child in set)
                    {
                        if(mods.ContainsKey(child))continue;
                        var id=child.ToString();
                        if(blocked?.Invoke(id)==true||!items.DonorMods.TryGetValue(id,out var donor)||donor.Count==0)continue;
                        var fill=donor.ToDictionary(kv=>kv.Key,kv=>kv.Value.Where(x=>x!=id).Select(x=>(MongoId)x).ToHashSet());
                        mods[child]=fill;next.Add(fill);
                        if(++added>=MaxParents)return added;
                    }
                }
            scan=next;
        }
        return added;
    }

    private static IReadOnlyDictionary<string,string[]> WithRequired(IReadOnlyDictionary<string,string[]> donor,
        IReadOnlyDictionary<string,string[]>? compat, IReadOnlySet<string>? required)
    {
        if(compat==null||required==null||required.Count==0)return donor;
        Dictionary<string,string[]>? merged=null;
        foreach(var(slot,ids) in compat)
        {
            if(!required.Any(r=>string.Equals(r,slot,StringComparison.OrdinalIgnoreCase)))continue;
            if(donor.Keys.Any(k=>string.Equals(k,slot,StringComparison.OrdinalIgnoreCase)))continue;
            merged??=new Dictionary<string,string[]>(donor);
            merged[slot]=ids;
        }
        return merged??donor;
    }
}
