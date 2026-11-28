using System.Text.Json.Nodes;

namespace Gravity.Core;

/// <summary>
/// 1.1.6 #106(사용자 11-28): 군즈(나이트·빅 파이프·버드아이)와 컬티스트(사제·전사·선구자·유령·오니, 즈랴치 컬티스트)를
/// 한 칸·'호위'에서 빼 각자 따로 정한다. 옛 저장값(boss_goons·cultist 한 칸, 보스·특수·호위 묶음 표)을 새 칸에 복사해 동작이 바뀌지 않게 한다.
/// 웹(index.html migrateSplit116)과 같은 규칙. 새 칸이 이미 있으면 건드리지 않는다.
/// </summary>
public static class RoleSplit
{
    public static readonly string[] Goons = ["boss_knight", "boss_bigpipe", "boss_birdeye"];
    public static readonly string[] Cults = ["cult_priest", "cult_warrior", "cult_predvestnik", "cult_prizrak", "cult_oni"];

    public static JsonNode? Migrate(JsonNode? state)
    {
        if (state is not JsonObject src) return state;
        var s = (JsonObject)src.DeepClone();
        void CopyRole(string from, string[] to)
        {
            if (s["roles"] is not JsonObject roles || roles[from] is not JsonNode old) return;
            foreach (var id in to) if (roles[id] == null) roles[id] = old.DeepClone();
        }
        CopyRole("boss_goons", Goons);
        CopyRole("cultist", Cults);

        var own = s["tierOwn"] as JsonObject;
        var data = s["tierData"] as JsonObject;
        if (data != null)
        {
            void CopyOwn(string from, string[] to)
            {
                if (own?[from]?.GetValueKind() != System.Text.Json.JsonValueKind.True || data[from] is not JsonArray t) return;
                foreach (var id in to)
                    if (data[id] == null) { data[id] = t.DeepClone(); own[id] = true; }
            }
            CopyOwn("boss_goons", Goons);
            CopyOwn("cultist", Cults);
            if (data["goons"] == null && data["boss"] is JsonArray boss) data["goons"] = boss.DeepClone();
            if (data["cultists"] == null)
            {
                if (data["special"] is JsonArray sp) data["cultists"] = sp.DeepClone();
                // 즈랴치 컬티스트는 전에 호위 묶음 표를 썼다 → 그 표를 전용 표로 가져가 그대로 쓴다.
                bool zOwn = own?["f_zryachiy"]?.GetValueKind() == System.Text.Json.JsonValueKind.True;
                if (!zOwn && data["follower"] is JsonArray fol && data["f_zryachiy"] == null)
                {
                    own ??= (JsonObject)(s["tierOwn"] = new JsonObject());
                    data["f_zryachiy"] = fol.DeepClone();
                    own["f_zryachiy"] = true;
                }
            }
        }

        if (s["blacklist"]?["entries"] is JsonArray entries)
            foreach (var e in entries.OfType<JsonObject>())
            {
                if (e["bots"] is not JsonArray bots) continue;
                var list = bots.OfType<JsonValue>().Select(v => v.ToString()).ToList();
                var add = new List<string>();
                if (list.Contains("boss_goons")) add.AddRange(Goons);
                if (list.Contains("cultist")) add.AddRange(Cults);
                if (list.Contains("boss")) add.Add("goons");
                if (list.Contains("special")) add.AddRange(Cults);
                if (list.Contains("follower")) add.Add("f_zryachiy");
                foreach (var a in add.Where(a => !list.Contains(a))) { bots.Add(a); list.Add(a); }
            }
        return s;
    }
}
