using System.Collections;
using System.Reflection;

namespace Gravity.Factions;

/// <summary>
/// 1.1.0 RUAF 검문소 '지키는 반경'(factions.ruaf.checkpoint.radius). RUAF 클라는 레이드 때 RUAF 서버 경로 /ruaf/checkpoints로
/// RUAF 설정(ConfigController.ModConfig)을 받아 검문소 좌표·반경을 쓴다. Gravity는 그 메모리 설정의 checkpointRadius만 바꾼다(파일은 그대로).
/// 0이면 RUAF 값(맵마다 다름)으로 돌린다. RUAF 형식이 다르거나 설정 객체가 하나가 아니면(싱글턴 아님) 아무것도 안 한다.
/// </summary>
public static class RuafLink
{
    private static object? _config;
    private static readonly Dictionary<object, double> Orig = new(ReferenceEqualityComparer.Instance);
    private static readonly object Lock = new();
    public static string? Status { get; private set; }

    /// <summary>DI에서 RUAF 설정 객체를 찾는다. 두 번 받아 같은 객체일 때만 쓴다.</summary>
    public static void Bind(IServiceProvider sp)
    {
        try
        {
            var t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RUAFComeHomeServer.ConfigController", false)).FirstOrDefault(x => x != null);
            if (t == null) { Status = "no RUAF"; return; }
            var a = sp.GetService(t);
            if (a == null || !ReferenceEquals(a, sp.GetService(t))) { Status = "RUAF config not a singleton"; return; }
            Use(t.GetField("ModConfig", BindingFlags.Public | BindingFlags.Instance)?.GetValue(a));
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    /// <summary>시험·Bind용: RUAF 설정(locations → checkpoint → checkpointZones → checkpointRadius 모양)을 쓴다.</summary>
    public static void Use(object? config)
    {
        lock (Lock) { _config = config; Orig.Clear(); }
        Status = config == null ? "RUAF config missing" : "ok";
    }

    /// <summary>모든 검문소 반경을 radius로(0 이하 = RUAF 원래 값). 바꾼 검문소 수.</summary>
    public static int ApplyRadius(double radius)
    {
        lock (Lock)
        {
            if (_config == null) return 0;
            int n = 0;
            try
            {
                if (Prop(_config, "locations") is not IDictionary locs) return 0;
                foreach (var map in locs.Values)
                {
                    if (Prop(Prop(map, "checkpoint"), "checkpointZones") is not IEnumerable zones) continue;
                    foreach (var z in zones)
                    {
                        var p = z?.GetType().GetProperty("checkpointRadius");
                        if (p == null || !p.CanWrite) continue;
                        if (!Orig.TryGetValue(z!, out var o)) Orig[z!] = o = Convert.ToDouble(p.GetValue(z));
                        var v = radius > 0 ? radius : o;
                        p.SetValue(z, Convert.ChangeType(v, p.PropertyType));
                        n++;
                    }
                }
            }
            catch (Exception ex) { Status = ex.Message; }
            return n;
        }
    }

    private static object? Prop(object? o, string name) => o?.GetType().GetProperty(name)?.GetValue(o);
}
