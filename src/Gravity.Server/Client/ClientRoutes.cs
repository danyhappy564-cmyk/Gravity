using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Gravity.Core;
using Gravity.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace Gravity.Client;

// SPT 라우터가 받는 요청 형식(IRequestData). 선은 camelCase(클라 플러그인이 같은 규칙으로 보낸다).
public sealed class HelloRequestData : IRequestData
{
    [JsonPropertyName("clientVersion")] public string ClientVersion { get; set; } = "";
    [JsonPropertyName("abpsClient")] public bool AbpsClient { get; set; }
}

public sealed class RaidRequestData : IRequestData
{
    [JsonPropertyName("mapId")] public string MapId { get; set; } = "";
}

public sealed class ReportRequestData : IRequestData
{
    [JsonPropertyName("mapId")] public string MapId { get; set; } = "";
    [JsonPropertyName("seed")] public int Seed { get; set; }
    [JsonPropertyName("spawned")] public Dictionary<string, int> Spawned { get; set; } = [];
    [JsonPropertyName("dropped")] public Dictionary<string, int> Dropped { get; set; } = [];
    [JsonPropertyName("sanitized")] public int Sanitized { get; set; }
    [JsonPropertyName("alivePerMinute")] public List<int[]> AlivePerMinute { get; set; } = [];
    [JsonPropertyName("killed")] public Dictionary<string, int> Killed { get; set; } = [];

    public RaidReport ToReport() => new(MapId, Seed, Spawned, Dropped, Sanitized, AlivePerMinute, Killed);
}

/// <summary>
/// 클라 플러그인용 경로 3개. `/gravity/...`는 웹 리스너가 전부 가져가므로 `/gravity-client/...`를 쓴다.
/// 응답은 camelCase JSON(Gravity.Shared 형식)을 JsonNode로 만들어 NoBody로 내보낸다(SPT 기본 직렬화는 PascalCase).
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class ClientRoutes : StaticRouter
{
    public const string HelloPath = "/gravity-client/hello";
    public const string RaidPath = "/gravity-client/raid";
    public const string ReportPath = "/gravity-client/report";

    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public ClientRoutes(JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil) : base(jsonUtil, Routes(httpResponseUtil)) { }

    internal static JsonNode? ToWire<T>(T value) => JsonSerializer.SerializeToNode(value, Wire);

    private static List<RouteAction> Routes(HttpResponseUtil http) =>
    [
        new RouteAction<HelloRequestData>(HelloPath, (url, info, sessionId, output, token) =>
        {
            var rt = GravityRuntime.Instance;
            var res = rt is null
                ? new HelloResponse(GravityVersion.Current, false, true)
                : rt.OnHello(sessionId.ToString(), new HelloRequest(info.ClientVersion, info.AbpsClient));
            return ValueTask.FromResult(http.NoBody(ToWire(res)));
        }),
        new RouteAction<RaidRequestData>(RaidPath, (url, info, sessionId, output, token) =>
        {
            var res = GravityRuntime.Instance?.ClientRaidFor(info.MapId) ?? ClientSettingsBuilder.Unmanaged(info.MapId);
            return ValueTask.FromResult(http.NoBody(ToWire(res)));
        }),
        new RouteAction<ReportRequestData>(ReportPath, (url, info, sessionId, output, token) =>
        {
            GravityRuntime.Instance?.OnReport(info.ToReport());
            return ValueTask.FromResult(http.NoBody(new JsonObject { ["ok"] = true }));
        })
    ];
}
