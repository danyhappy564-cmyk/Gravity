using System;

namespace Gravity.Shared
{
    /// <summary>서버와 클라 판 번호 비교. 앞 두 자리(주.부)가 같으면 호환.</summary>
    public static class GravityVersion
    {
        public const string Current = "1.1.0";

        public static bool Compatible(string? a, string? b)
        {
            if (!TryParse(a, out var a1, out var a2) || !TryParse(b, out var b1, out var b2)) return false;
            return a1 == b1 && a2 == b2;
        }

        private static bool TryParse(string? v, out int major, out int minor)
        {
            major = minor = 0;
            if (string.IsNullOrWhiteSpace(v)) return false;
            var parts = v!.Trim().Split('.');
            if (parts.Length < 2) return false;
            return int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor)
                && major >= 0 && minor >= 0;
        }
    }
}
