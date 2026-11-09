using System.Reflection;
using Gravity.Spawn;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Profile;
using SPTarkov.Server.Core.Services.Server;
using SPTarkov.Server.Core.Models.Enums;

namespace Gravity.Core;

/// <summary>서버 데이터가 다 올라온 뒤 한 번: 설정·감지·맵 정보 준비, 패치 켜기.</summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class GravityLoader(
    ISptLogger<GravityLoader> logger, ModHelper modHelper, LocationTable locations,
    PmcConfig pmcConfig, BotConfig botConfig, ProfileActivityService activity, SeasonalEventService seasonal) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        GravityBoot.Ensure(modHelper);
        var paths = GravityBoot.Paths!;
        GravityWebPaths.Paths = paths;
        var store = GravityBoot.Store!;
        var load = GravityBoot.Load!;
        if (!load.Ok) logger.Warning($"{GravityInfo.Tag} {load.Error}");
        var detect = GravityBoot.Detect!;

        var catalog = new MapCatalog();
        catalog.Build(locations, pmcConfig, botConfig);

        GravityRuntime.Instance = new GravityRuntime(store, catalog, detect, m => logger.Warning(m), m => logger.Info(m))
        {
            MaxBotCap = botConfig.MaxBotCap,
            BotCfg = botConfig,
            ActiveEvents = () =>
            {
                var types = seasonal.GetActiveEvents().Select(e => e.Type).ToHashSet();
                bool af = types.Contains(SeasonalEventType.AprilFools);
                return (af || types.Contains(SeasonalEventType.Halloween),
                    af || types.Contains(SeasonalEventType.Christmas) || types.Contains(SeasonalEventType.NewYears));
            }
        };
        RaidStartPatch.Activity = activity;
        new RaidStartPatch().Enable();

        logger.Info($"{GravityInfo.Tag} {GravityInfo.Version} 로드: 맵 {catalog.All.Count}개, 설정 {(store.RawState == null ? "기본값" : "저장본")}, 웹 /gravity/");
        var found = new List<string>();
        if (detect.Abps) found.Add("ABPS(스폰 모듈 잠금)");
        if (detect.Apbs) found.Add("APBS");
        if (detect.Svm) found.Add("SVM");
        if (detect.Orbit) found.Add("ORBIT");
        if (detect.Sain) found.Add("SAIN");
        if (detect.Ruaf) found.Add("RUAF");
        if (detect.Untar) found.Add("UNTAR");
        if (detect.BlackDiv) found.Add("BlackDiv");
        if (detect.QuestingBots) found.Add("QuestingBots(지원 안 함)");
        if (found.Count > 0) logger.Info($"{GravityInfo.Tag} 감지: {string.Join(", ", found)}");
        return Task.CompletedTask;
    }
}

/// <summary>웹 리스너가 쓸 경로(로더가 채움).</summary>
public static class GravityWebPaths
{
    public static GravityPaths? Paths { get; set; }
}
