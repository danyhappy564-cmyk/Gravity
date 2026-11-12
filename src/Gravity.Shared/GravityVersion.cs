using System;

namespace Gravity.Shared
{
    /// <summary>앞 두 자리와 시작 웨이브 전달 방식의 세대를 비교합니다. 1.0.3 전후는 서로 호환하지 않습니다.</summary>
    public static class GravityVersion
    {
        public const string Current = "1.0.4";

        public static bool Compatible(string? a, string? b)
        {
            if (!TryParse(a, out var a1, out var a2, out var a3) || !TryParse(b, out var b1, out var b2, out var b3)) return false;
            if (a1 != b1 || a2 != b2) return false;
            // 1.0.3에서 시작 스캐브 표식·대기 시각 전달이 바뀌었습니다. 이전 판을 같은 부 버전으로 허용하지 않습니다.
            return a1 != 1 || a2 != 0 || (a3 >= 3) == (b3 >= 3);
        }

        /// <summary>구형 서버가 호환이라고 답해도 클라 자신의 전달 방식과 한 번 더 비교합니다.</summary>
        public static HelloResponse ValidateHello(HelloResponse response) =>
            response with { Compatible = response.Compatible && Compatible(Current, response.ServerVersion) };

        private static bool TryParse(string? v, out int major, out int minor, out int patch)
        {
            major = minor = patch = 0;
            if (string.IsNullOrWhiteSpace(v)) return false;
            var parts = v!.Trim().Split('.');
            if (parts.Length < 2) return false;
            return int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor)
                && (parts.Length < 3 || int.TryParse(parts[2], out patch))
                && major >= 0 && minor >= 0 && patch >= 0;
        }
    }
}
