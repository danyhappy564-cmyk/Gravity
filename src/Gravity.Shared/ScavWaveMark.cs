using System;
using System.Globalization;

namespace Gravity.Shared
{
    /// <summary>Gravity 시작 스캐브 웨이브의 인원·단일 구역을 클라의 봇 양 변환 뒤까지 보존합니다.</summary>
    public static class ScavWaveMark
    {
        public const string Prefix = "~gravity.scav/";

        public static string Encode(int min, int max, string zone) =>
            Prefix + min.ToString(CultureInfo.InvariantCulture) + "/" + max.ToString(CultureInfo.InvariantCulture) + "/" + zone;

        public static bool TryDecode(string? value, out int min, out int max, out string zone)
        {
            min = max = 0; zone = "";
            if (value == null || value.Length > 4096 || !value.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            var parts = value.Substring(Prefix.Length).Split(new[] { '/' }, 3);
            if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out min)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out max)
                || min < 0 || max < min || max > 4096) return false;
            zone = parts[2];
            return true;
        }
    }
}
