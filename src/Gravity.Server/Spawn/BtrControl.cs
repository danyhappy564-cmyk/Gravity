using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Gravity.Spawn;

/// <summary>1.1.0(#67) BTR 한 맵: 나올 확률(%)과 나오는 시각 범위(레이드 시작 뒤 초).</summary>
public sealed record BtrMap(double Chance, double MinSec, double MaxSec);

/// <summary>BTR 설정(웹 btr.*). On이 꺼져 있으면 SPT 원래 값 그대로. 원래 BTR이 나오는 삼림·스트리트만.</summary>
public sealed record BtrSettings(bool On = false, BtrMap? Woods = null, BtrMap? Streets = null)
{
    public static readonly BtrMap WoodsDefault = new(100, 180, 360), StreetsDefault = new(100, 300, 600);
}

/// <summary>
/// 1.1.0(#67) BTR 빈도. 레이드 시작 응답(StartLocalRaidResponseData.ServerSettings)에 SPT 메모리 표 templates.locationServices가 그대로 실리므로
/// 그 표의 BTRServerSettings.ServerMapBTRSettings에서 Woods·TarkovStreets 두 항목의 ChanceSpawn·SpawnPeriod(x=최소, y=최대 초)만 바꾼다.
/// BTR이 나오는 맵 목록(전역 BTRSettings.LocationsWithBTR)이나 다른 항목·다른 맵은 건드리지 않는다. 꺼지면 처음 읽은 원래 값으로 되돌린다.
/// </summary>
public static class BtrControl
{
    public static readonly string[] Maps = ["Woods", "TarkovStreets"];
    private static readonly Dictionary<string, (double Chance, Vector3 Period)> Orig = new(StringComparer.Ordinal);
    private static readonly object Lock = new();

    /// <summary>바꾼(또는 되돌린) 맵 수. 표가 없으면 0.</summary>
    public static int Apply(Dictionary<string, ServerMapBtrsettings>? table, BtrSettings s)
    {
        if (table == null) return 0;
        lock (Lock)
        {
            int n = 0;
            foreach (var id in Maps)
            {
                if (!table.TryGetValue(id, out var m) || m == null) continue;
                if (!Orig.ContainsKey(id)) Orig[id] = (m.ChanceSpawn, m.SpawnPeriod);
                var (oc, op) = Orig[id];
                var want = s.On ? (id == "Woods" ? s.Woods ?? BtrSettings.WoodsDefault : s.Streets ?? BtrSettings.StreetsDefault) : null;
                m.ChanceSpawn = want == null ? oc : Math.Clamp(want.Chance, 0, 100);
                m.SpawnPeriod = want == null ? op : new Vector3 { X = (float)Math.Min(want.MinSec, want.MaxSec), Y = (float)Math.Max(want.MinSec, want.MaxSec), Z = op.Z };
                n++;
            }
            return n;
        }
    }

    /// <summary>시험용: 기억한 원래 값을 지운다.</summary>
    internal static void Forget() { lock (Lock) Orig.Clear(); }
}
