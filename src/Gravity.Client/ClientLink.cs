using System;
using System.Threading.Tasks;
using Gravity.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using SPT.Common.Http;

namespace Gravity.Client
{
    /// <summary>
    /// 서버 Gravity와 주고받기(SPT RequestHandler). 선은 camelCase JSON, 퍼센트는 0~1 비율.
    /// 실패하면 null을 돌려주고 경고 한 줄. 게임 스레드에서는 Async 판만 부른다(동기 판은 응답을 기다린다).
    /// </summary>
    public static class ClientLink
    {
        public const string HelloPath = "/gravity-client/hello";
        public const string RaidPath = "/gravity-client/raid";
        public const string ReportPath = "/gravity-client/report";

        /// <summary>속성 이름만 camelCase. 사전 키(구역 이름, pmcUSEC 등)는 그대로 둔다.
        /// 레코드는 매개변수 있는 public 생성자가 하나뿐이라 Newtonsoft가 그 생성자로 만든다(이름은 대소문자 무시로 맞춤).</summary>
        internal static readonly JsonSerializerSettings Wire = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false, OverrideSpecifiedNames = true }
            },
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include
        };

        public static HelloResponse? Hello()
        {
            try { return ParseHello(RequestHandler.PostJson(HelloPath, HelloBody())); }
            catch (Exception ex) { return Fail<HelloResponse>("hello", ex); }
        }

        public static async Task<HelloResponse?> HelloAsync()
        {
            try { return ParseHello(await RequestHandler.PostJsonAsync(HelloPath, HelloBody()).ConfigureAwait(false)); }
            catch (Exception ex) { return Fail<HelloResponse>("hello", ex); }
        }

        public static ClientRaidSettings? FetchRaid(string mapId)
        {
            try { return ParseRaid(RequestHandler.PostJson(RaidPath, RaidBody(mapId))); }
            catch (Exception ex) { return Fail<ClientRaidSettings>("raid", ex); }
        }

        public static async Task<ClientRaidSettings?> FetchRaidAsync(string mapId)
        {
            try { return ParseRaid(await RequestHandler.PostJsonAsync(RaidPath, RaidBody(mapId)).ConfigureAwait(false)); }
            catch (Exception ex) { return Fail<ClientRaidSettings>("raid", ex); }
        }

        /// <summary>보고는 기다리지 않고 보낸다(레이드 끝 화면을 막지 않으려고).</summary>
        public static void SendReport(RaidReport r)
        {
            string body;
            try { body = Serialize(r); }
            catch (Exception ex) { Fail<object>("report", ex); return; }
            _ = SendReportAsync(body);
        }

        private static async Task SendReportAsync(string body)
        {
            try { await RequestHandler.PostJsonAsync(ReportPath, body).ConfigureAwait(false); }
            catch (Exception ex) { Fail<object>("report", ex); }
        }

        internal static string Serialize<T>(T value) => JsonConvert.SerializeObject(value, Wire);

        internal static T? Deserialize<T>(string json) where T : class => JsonConvert.DeserializeObject<T>(json, Wire);

        private static string HelloBody() => Serialize(new HelloRequest(GravityVersion.Current, Plugin.AbpsPresent));

        private static string RaidBody(string mapId) => JsonConvert.SerializeObject(new { mapId }, Wire);

        private static HelloResponse? ParseHello(string json)
        {
            var r = string.IsNullOrWhiteSpace(json) ? null : Deserialize<HelloResponse>(json);
            if (r == null || string.IsNullOrEmpty(r.ServerVersion))
            {
                Log.Warn("서버 연결 실패: hello 응답이 비었음(서버 Gravity 0.2가 없거나 꺼짐)");
                return null;
            }
            return GravityVersion.ValidateHello(r);
        }

        private static ClientRaidSettings? ParseRaid(string json)
        {
            var r = string.IsNullOrWhiteSpace(json) ? null : Deserialize<ClientRaidSettings>(json);
            if (r == null)
            {
                Log.Warn("서버 연결 실패: raid 응답이 비었음");
                return null;
            }
            return r;
        }

        private static T? Fail<T>(string what, Exception ex) where T : class
        {
            Log.Warn($"서버 연결 실패({what}): {ex.GetBaseException().Message}");
            return null;
        }
    }
}
