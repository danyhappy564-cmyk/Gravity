using System.Net;
using System.Security.Claims;
using Gravity.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gravity.Tests;

/// <summary>1.0.5: 다른 IP(Fika·내부망)로 연 웹도 SPT 웹 관리자로 로그인했으면 저장할 수 있다(외부 제보, INBOX 38).</summary>
public class WriteAccessTests
{
    private static ClaimsPrincipal User(bool admin) => new(new ClaimsIdentity(
        admin ? [new Claim(ClaimTypes.Name, "spt"), new Claim("isAdministrator", "true")] : [new Claim(ClaimTypes.Name, "guest")], "SptWebCookie"));

    private static HttpContext Ctx(string ip, ClaimsPrincipal? user = null)
    {
        var c = new DefaultHttpContext();
        c.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        c.Connection.LocalIpAddress = IPAddress.Parse("192.168.0.5");
        if (user != null) c.User = user;
        return c;
    }

    [Fact]
    public void 규칙_이_PC_또는_SPT_로그인_꺼짐_또는_관리자()
    {
        Assert.True(WriteAccess.Allowed(true, true, null));
        Assert.True(WriteAccess.Allowed(false, false, null));
        Assert.True(WriteAccess.Allowed(false, true, User(true)));
        Assert.False(WriteAccess.Allowed(false, true, User(false)));
        Assert.False(WriteAccess.Allowed(false, true, null));
    }

    [Fact]
    public async Task 요청으로_판정한다()
    {
        Assert.True(await WriteAccess.CheckAsync(Ctx("127.0.0.1"), authEnabled: true));
        Assert.True(await WriteAccess.CheckAsync(Ctx("::ffff:127.0.0.1"), authEnabled: true));
        Assert.False(await WriteAccess.CheckAsync(Ctx("203.0.113.7"), authEnabled: true));
        Assert.True(await WriteAccess.CheckAsync(Ctx("203.0.113.7", User(true)), authEnabled: true));
        Assert.False(await WriteAccess.CheckAsync(Ctx("203.0.113.7", User(false)), authEnabled: true));
        Assert.True(await WriteAccess.CheckAsync(Ctx("203.0.113.7"), authEnabled: false));
        // 같은 PC를 내부망 주소로 연 경우(원격 주소 = 서버 주소)도 이 PC로 본다 — SPT AuthService.IsLocalRequest와 같은 규칙.
        Assert.True(await WriteAccess.CheckAsync(Ctx("192.168.0.5"), authEnabled: true));
    }

    // 실제 웹 서버(Kestrel)로 SPT와 같은 쿠키 로그인 → 다른 주소의 저장 요청 판정까지 왕복한다.
    // SPT가 모드 리스너를 인증 미들웨어 앞에 두든 뒤에 두든(useAuthMiddleware) 같은 결과여야 한다.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SPT_쿠키_로그인_뒤_다른_주소에서도_저장된다(bool useAuthMiddleware)
    {
        var b = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        b.WebHost.UseUrls("http://127.0.0.1:0");
        b.Services.AddAuthentication(WriteAccess.Scheme).AddCookie(WriteAccess.Scheme, o => o.Cookie.Name = "SPT.Server.Web.Auth");
        await using var app = b.Build();
        app.Use(async (ctx, next) => { ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7"); await next(); });
        if (useAuthMiddleware) app.UseAuthentication();
        // SPT 로그인(/spt-web-auth/login)과 Gravity 저장(/gravity/api/state)을 흉내 내는 끝 미들웨어.
        app.Run(async ctx =>
        {
            if (ctx.Request.Path == "/spt-web-auth/login") { await ctx.SignInAsync(WriteAccess.Scheme, User(ctx.Request.Query["admin"] == "1")); return; }
            ctx.Response.StatusCode = await WriteAccess.CheckAsync(ctx, authEnabled: true) ? 200 : 403;
            await ctx.Response.WriteAsync($"{ctx.Connection.RemoteIpAddress}|{ctx.User?.Identity?.IsAuthenticated}");
        });
        await app.StartAsync();
        var url = app.Urls.First();
        using var http = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(url) };
        var first = await http.PostAsync("/gravity/api/state", null);
        Assert.True(first.StatusCode == HttpStatusCode.Forbidden, url + " " + first.StatusCode + " [" + await first.Content.ReadAsStringAsync() + "]");
        await http.PostAsync("/spt-web-auth/login?admin=0", null);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync("/gravity/api/state", null)).StatusCode);
        await http.PostAsync("/spt-web-auth/login?admin=1", null);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/gravity/api/state", null)).StatusCode);
        await app.StopAsync();
    }

    [Fact]
    public void 로그인_주소는_Gravity로_돌아온다() => Assert.Equal("/login?returnUrl=%2Fgravity%2F", WriteAccess.LoginUrl);
}
