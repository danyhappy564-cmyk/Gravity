using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gravity.Core;
using Microsoft.AspNetCore.Http;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers.Http;

namespace Gravity.Web;

/// <summary>/gravity/ 화면(정적 파일)과 /gravity/api/* JSON. SPT 기본 리스너보다 먼저 묻는다(우선순위 0).</summary>
[Injectable(TypePriority = 0)]
public class GravityWeb : IHttpListener
{
    private const string Prefix = "/gravity";
    private const int MaxBody = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8", [".js"] = "text/javascript; charset=utf-8", [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8", [".svg"] = "image/svg+xml", [".png"] = "image/png", [".ico"] = "image/x-icon"
    };

    public bool CanHandle(HttpContext context)
    {
        var p = context.Request.Path.Value ?? "";
        return p.Equals(Prefix, StringComparison.OrdinalIgnoreCase) || p.StartsWith(Prefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(MongoId sessionId, HttpContext context, CancellationToken cancellationToken = default)
    {
        var req = context.Request;
        var res = context.Response;
        res.Headers.CacheControl = "no-store";
        var path = req.Path.Value ?? "";
        try
        {
            if (path.Equals(Prefix, StringComparison.OrdinalIgnoreCase)) { res.Redirect(Prefix + "/"); return; }
            var rt = GravityRuntime.Instance;
            var api = path[(Prefix.Length + 1)..];
            if (api.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            {
                if (rt is null) { await Send(res, 503, new { error = "Gravity가 아직 준비되지 않았습니다" }); return; }
                switch (api[4..].ToLowerInvariant(), req.Method)
                {
                    case ("state", "GET"):
                        await Send(res, 200, new { state = rt.Store.RawState, loadWarning = rt.Store.LoadWarning });
                        return;
                    case ("state", "POST"):
                        if (!IsLoopback(context.Connection.RemoteIpAddress)) { await Send(res, 403, new { error = "이 PC에서만 저장할 수 있습니다" }); return; }
                        if (req.ContentLength > MaxBody) { await Send(res, 413, new { error = "너무 큽니다" }); return; }
                        JsonNode? node;
                        try { node = await JsonNode.ParseAsync(req.Body, cancellationToken: cancellationToken); }
                        catch (JsonException ex) { await Send(res, 400, new { ok = false, error = "JSON 형식 오류: " + ex.Message }); return; }
                        var state = node?["state"] ?? node;
                        if (state is not JsonObject) { await Send(res, 400, new { ok = false, error = "state 객체가 필요합니다" }); return; }
                        var saved = rt.Store.Save(state);
                        await Send(res, saved.Ok ? 200 : 400, new { ok = saved.Ok, error = saved.Error, clamped = saved.Clamped });
                        return;
                    case ("meta", "GET"):
                        await Send(res, 200, new
                        {
                            version = GravityInfo.Version, detect = rt.Detect,
                            locked = new { spawn = rt.Detect.SpawnLocked, gear = rt.Detect.GearLocked },
                            maps = rt.Catalog.All
                        });
                        return;
                    case ("lastraid", "GET"):
                        await Send(res, 200, rt.Last is { } l ? new { at = l.At, summary = l.Summary, skipped = l.Skipped, plan = l.Plan } : null);
                        return;
                }
                await Send(res, 404, new { error = "없는 API" });
                return;
            }

            var root = GravityWebPaths.Paths?.WwwRoot;
            var file = root is null ? null : ResolveStatic(root, api);
            if (file is null || !File.Exists(file)) { res.StatusCode = 404; return; }
            res.ContentType = Types.GetValueOrDefault(Path.GetExtension(file), "application/octet-stream");
            await res.SendFileAsync(file, cancellationToken);
        }
        catch (Exception ex) when (!res.HasStarted)
        {
            await Send(res, 500, new { error = ex.Message });
        }
    }

    /// <summary>wwwroot 안 파일 경로. 빈 경로는 index.html, 밖으로 나가는 경로는 null.</summary>
    internal static string? ResolveStatic(string wwwroot, string relative)
    {
        relative = Uri.UnescapeDataString(relative ?? "").Replace((char)92, '/').TrimStart('/');
        if (relative.Length == 0) relative = "index.html";
        if (relative.Split('/').Any(seg => seg == ".." || seg.Contains(':'))) return null;
        var rootFull = Path.GetFullPath(wwwroot);
        var full = Path.GetFullPath(Path.Combine(rootFull, relative));
        return full.StartsWith(rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static bool IsLoopback(IPAddress? ip) => ip is not null && IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip);

    private static async Task Send(HttpResponse res, int status, object? body)
    {
        res.StatusCode = status;
        res.ContentType = "application/json; charset=utf-8";
        await res.WriteAsync(JsonSerializer.Serialize(body, Json), Encoding.UTF8);
    }
}
