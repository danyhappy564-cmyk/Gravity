using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SPTarkov.Server.Core.Models.Spt.Config;

namespace Gravity.Web;

/// <summary>
/// 웹에서 설정을 바꾸는 요청(저장·프리셋 목록·장비 재계산)을 받아도 되는지. SPT 4.1 웹 로그인과 같은 규칙(1.0.5, 외부 제보):
/// 이 PC에서 열었거나(원격 주소가 루프백 또는 서버 자신의 주소), SPT 웹 로그인이 꺼져 있거나(<c>http.json webAuthenticationConfig.enabled</c>),
/// SPT 웹 관리자로 로그인했으면(쿠키 <c>SptWebCookie</c>, claim <c>isAdministrator=true</c>) 허용한다.
/// </summary>
public static class WriteAccess
{
    public const string Scheme = "SptWebCookie";
    /// <summary>SPT 웹 로그인 화면 — 로그인 뒤 Gravity 화면으로 돌아온다.</summary>
    public const string LoginUrl = "/login?returnUrl=%2Fgravity%2F";

    public static bool Allowed(bool local, bool authEnabled, ClaimsPrincipal? user) =>
        local || !authEnabled || user?.HasClaim("isAdministrator", "true") == true;

    /// <summary>SPT AuthService.IsLocalRequest와 같은 판정: 루프백이거나 원격 주소 = 서버 주소.</summary>
    public static bool IsLocal(ConnectionInfo c)
    {
        static IPAddress? Norm(IPAddress? ip) => ip is { IsIPv4MappedToIPv6: true } ? ip.MapToIPv4() : ip;
        var remote = Norm(c.RemoteIpAddress);
        if (remote is null || IPAddress.IsLoopback(remote)) return true;
        var local = Norm(c.LocalIpAddress);
        return local is not null && remote.Equals(local);
    }

    /// <param name="authEnabled">시험용. 없으면 SPT HttpConfig에서 읽고, 못 읽으면 켜짐으로 본다(안전한 쪽).</param>
    public static async Task<bool> CheckAsync(HttpContext ctx, bool? authEnabled = null)
    {
        if (IsLocal(ctx.Connection)) return true;
        var enabled = authEnabled ?? ctx.RequestServices?.GetService<HttpConfig>()?.WebAuthenticationConfig?.Enabled ?? true;
        if (!enabled) return true;
        var user = ctx.User;
        if (user?.Identity?.IsAuthenticated != true && ctx.RequestServices?.GetService<IAuthenticationService>() != null)
        {
            try { var r = await ctx.AuthenticateAsync(Scheme); if (r.Succeeded) user = r.Principal; }
            catch (InvalidOperationException) { /* SPT 웹 인증이 없는 서버 */ }
        }
        return Allowed(false, true, user);
    }
}
