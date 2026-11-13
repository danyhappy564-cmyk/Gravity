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
    private const int MaxBody = 16 * 1024 * 1024; // 원작 에드온 프리셋 적용 상태가 약 2.8MB(봇 27종 × 7티어 표)
    /// <summary>내 프리셋 목록(장비 프리셋은 티어 표를 통째로 담아 커서 따로, Codex 0.6 지적 7).</summary>
    private const int MaxLibrary = 64 * 1024 * 1024; // 원작 프리셋을 바탕으로 만든 내 장비 프리셋 하나가 약 3MB

    /// <summary>본문을 최대 max바이트까지만 읽어 JSON으로. 넘으면 InvalidDataException(Content-Length 없는 요청도 막음).</summary>
    internal static async Task<JsonNode?> ReadJson(HttpRequest req, int max, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await req.Body.ReadAsync(buf, ct)) > 0)
        {
            if (ms.Length + n > max) throw new InvalidDataException("too large");
            ms.Write(buf, 0, n);
        }
        ms.Position = 0;
        return await JsonNode.ParseAsync(ms, cancellationToken: ct);
    }
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal static JsonSerializerOptions JsonOptions => Json;

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
                        try { node = await ReadJson(req, MaxBody, cancellationToken); }
                        catch (InvalidDataException) { await Send(res, 413, new { ok = false, error = "너무 큽니다" }); return; }
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
                            maps = rt.Catalog.All,
                            automaticVariants = rt.Catalog.AutomaticVariants.OrderBy(id => id).ToArray(),
                            activeVariants = rt.ActiveVariants?.OrderBy(id => id).ToArray(),
                            client = ClientBody(rt),
                            events = new
                            {
                                owner = Gravity.Events.GravityEventLoader.ResolvedOwner,
                                svm = Gravity.Events.GravityEventLoader.Svm,
                                applied = Gravity.Events.GravityEventLoader.Applied
                            },
                            factions = new
                            {
                                ruaf = new { installed = rt.Detect.Ruaf, maps = rt.Factions.Ruaf.Keys.ToArray(),
                                    checkpointMaps = rt.Factions.Ruaf.Where(kv => kv.Value.CheckpointZones.Length > 0).Select(kv => kv.Key).ToArray() },
                                untar = new { installed = rt.Detect.Untar },
                                blackdiv = new { installed = rt.Detect.BlackDiv, maps = rt.Factions.BlackDivMaps },
                                blackout = new { installed = rt.Factions.BlackoutChance != null, chance = rt.Factions.BlackoutChance, now = rt.Factions.BlackoutNow() },
                                otherPmc = rt.LastOtherPmc, pmcSigMaps = rt.Factions.SptPmc.Count
                            },
                            gear = GearBody(rt),
                            orbit = rt.Orbit,
                            lockReason = rt.Detect.SpawnLockReason,
                            layout = rt.LastLayout,
                            recent = DiagLog.RecentEntries().Select(e => new { at = e.At.ToString("HH:mm:ss"), level = e.Level, text = e.Text }),
                            reports = new { dir = DiagLog.ReportsDir, last = rt.LastReportFile }
                        });
                        return;
                    case ("library", "GET"):
                        await Send(res, 200, new { library = rt.Library?.Current, loadWarning = rt.Library?.LoadWarning });
                        return;
                    case ("library", "POST"):
                    {
                        if (!IsLoopback(context.Connection.RemoteIpAddress)) { await Send(res, 403, new { error = "이 PC에서만 저장할 수 있습니다" }); return; }
                        if (req.ContentLength > MaxLibrary) { await Send(res, 413, new { ok = false, error = "너무 큽니다" }); return; }
                        if (rt.Library is null) { await Send(res, 503, new { ok = false, error = "프리셋 저장소 없음" }); return; }
                        JsonNode? lib;
                        try { lib = await ReadJson(req, MaxLibrary, cancellationToken); }
                        catch (InvalidDataException) { await Send(res, 413, new { ok = false, error = "너무 큽니다" }); return; }
                        catch (JsonException ex) { await Send(res, 400, new { ok = false, error = "JSON 형식 오류: " + ex.Message }); return; }
                        var r = rt.Library.Save(lib?["library"] ?? lib);
                        await Send(res, r.Ok ? 200 : 400, new { ok = r.Ok, error = r.Error });
                        return;
                    }
                    case ("gear/rebuild", "POST"):
                    {
                        if (!IsLoopback(context.Connection.RemoteIpAddress)) { await Send(res, 403, new { error = "이 PC에서만 할 수 있습니다" }); return; }
                        var sum = Gravity.Gear.GearRuntime.RebuildItems();
                        if (sum != null) rt.Note($"{GravityInfo.Tag} {L.T("장비 아이템 티어 다시 계산", "Gear item tiers recalculated")}: {sum}");
                        await Send(res, sum != null ? 200 : 503, new { ok = sum != null, summary = sum, error = sum == null ? "장비 자료가 준비되지 않았습니다" : null });
                        return;
                    }
                    case ("dev/sim", "GET"):
                    {
                        var map = req.Query["map"].ToString();
                        int.TryParse(req.Query["n"].ToString(), out var n);
                        var sim = rt.Simulate(map, n <= 0 ? 10 : n, Random.Shared.Next(1, int.MaxValue));
                        await Send(res, sim != null ? 200 : 404, sim != null ? new { rows = sim.Rows, skip = sim.Skip, client = sim.Client } : new { error = "모르는 맵" });
                        return;
                    }
                    case ("dev/plan", "GET"):
                    {
                        var map = req.Query["map"].ToString();
                        var plan = rt.PlanPreview(map, int.TryParse(req.Query["seed"].ToString(), out var sd) && sd > 0 ? sd : Random.Shared.Next(1, int.MaxValue));
                        await Send(res, plan != null ? 200 : 404, plan ?? new { error = "모르는 맵" });
                        return;
                    }
                    case ("reports", "GET"):
                        await Send(res, 200, new { dir = DiagLog.ReportsDir, files = DiagLog.ReportFiles(), last = rt.LastReportFile });
                        return;
                    case ("items", "GET"):
                        await Send(res, 200, new { items = Gravity.Gear.GearRuntime.Search(req.Query["q"].ToString(), req.Query["cat"].ToString()) });
                        return;
                    case ("gear/catalog", "GET"):
                        var gear = rt.Store.Current.Gear;
                        await Send(res, 200, new { items = Gravity.Gear.GearRuntime.Catalog().Select(i => i with
                            { ManualTier = gear?.ItemTier.TryGetValue(i.Id, out var t) == true ? t : null }),
                            clothing = Gravity.Gear.GearRuntime.Items.Clothing.Select(c => new { id = c.Id, source = c.Source, part = c.Part, sides = c.Sides }) });
                        return;
                    case ("lastraid", "GET"):
                        await Send(res, 200, LastRaidBody(rt));
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

    /// <summary>`api/meta`의 gear 칸: 장비 모듈 상태·아이템 자료·이번 판 티어 분포.</summary>
    internal static object GearBody(GravityRuntime rt)
    {
        var gi = Gravity.Gear.GearRuntime.Items;
        var raid = Gravity.Gear.GearRuntime.Raid;
        return new
        {
            active = Gravity.Gear.GearRuntime.Active() != null, locked = rt.Detect.GearLocked,
            items = gi.Info.Count, slots = gi.SlotTier.Count,
            mods = new { weapons = gi.ModWeapons.Count, equipment = gi.ModEquipment.Count, attachments = gi.ModAttachments.Count },
            raid = new { subset = raid.Allowed != null, weapons = raid.Weapons, equipment = raid.Equipment, attachments = raid.Attachments },
            dressed = Gravity.Gear.GearRuntime.Dressed,
            tiers = Gravity.Gear.GearRuntime.Stats.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()),
            error = Gravity.Gear.GearRuntime.LastError
        };
    }

    /// <summary>`api/meta`의 client 칸: 마지막 Hello 기준. 클라가 한 번도 안 왔으면 version은 null.</summary>
    internal static JsonObject ClientBody(GravityRuntime rt)
    {
        var s = rt.Sessions;
        var v = s.LastVersion;
        return new JsonObject
        {
            ["version"] = v,
            ["compatible"] = v is not null && Gravity.Shared.GravityVersion.Compatible(v, Gravity.Shared.GravityVersion.Current),
            ["abps"] = s.LastAbps,
            ["lastHello"] = s.LastHelloAt
        };
    }

    /// <summary>`api/lastraid` 응답: 마지막 계획과 클라 보고(없으면 report는 null). 둘 다 없으면 null.</summary>
    internal static object? LastRaidBody(GravityRuntime rt)
    {
        var l = rt.Last;
        var r = rt.LastReport;
        if (l is null && r is null) return null;
        object? report = null;
        if (r is not null)
        {
            var p = rt.LastReportPlan;
            report = new
            {
                at = rt.LastReportAt, mapId = r.MapId, seed = r.Seed,
                spawned = r.Spawned, dropped = r.Dropped, sanitized = r.Sanitized, alivePerMinute = r.AlivePerMinute, killed = r.Killed,
                extracted = r.Extracted, extractRefill = r.ExtractRefill, layout = rt.LastReportLayout,
                plan = p is null ? null : new
                {
                    startPmcs = p.StartPmcs, wavePmcs = p.PmcTotal - p.StartPmcs, pmcTotal = p.PmcTotal,
                    usec = p.Groups.Where(g => g.Usec).Sum(g => g.Size), bear = p.Groups.Where(g => !g.Usec).Sum(g => g.Size),
                    droppedPmcs = p.DroppedPmcs
                }
            };
        }
        return new { at = l?.At, summary = l?.Summary, skipped = l?.Skipped, plan = l?.Plan, report };
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
