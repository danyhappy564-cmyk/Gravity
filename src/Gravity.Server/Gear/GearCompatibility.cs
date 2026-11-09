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
            if(!seen.Add(id)||depth>=MaxDepth||!items.CompatibleMods.TryGetValue(id,out var slots))continue;
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
}
