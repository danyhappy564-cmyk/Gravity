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
using SPTarkov.Server.Core.Utils;

namespace Gravity.Core;

/// <summary>서버 데이터가 다 올라온 뒤 한 번: 설정·감지·맵 정보 준비, 패치 켜기.</summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class GravityLoader(
    ISptLogger<GravityLoader> logger, ModHelper modHelper, LocationTable locations,
    PmcConfig pmcConfig, BotConfig botConfig, ProfileActivityService activity, SeasonalEventService seasonal, JsonUtil json,
    TemplateTable templates, BotTable bots, GlobalTable globals, SPTarkov.Server.Core.Services.Locales.LocaleService locales) : IOnLoad
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
        // 0.4: 팩션 모드 설정·SPT 원본 PMC 지문(읽기 전용). SPT_Data는 user/mods에서 두 단계 위.
        var sptData = new[] { Path.Combine(GravityBoot.ModsDir!, "..", "..", "SPT_Data"), Path.Combine(AppContext.BaseDirectory, "SPT_Data") }
            .Select(Path.GetFullPath).FirstOrDefault(Directory.Exists);
        GravityRuntime.Instance.Factions = Gravity.Factions.FactionData.Load(GravityBoot.ModsDir!, sptData, ConfigModel.KnownMaps,
            t => json.Deserialize<List<SPTarkov.Server.Core.Models.Eft.Common.BossLocationSpawn>>(t), m => logger.Warning($"{GravityInfo.Tag} {m}"));
        var fd = GravityRuntime.Instance.Factions;
        if (sptData == null) logger.Warning($"{GravityInfo.Tag} SPT_Data 폴더를 못 찾아 다른 모드 PMC 구분·연구소 게이트 복원을 끕니다");
        RaidStartPatch.Activity = activity;
        new RaidStartPatch().Enable();

        // 0.5 장비: 아이템 정보·자동 티어(읽기만)와 봇 생성 패치. APBS가 있으면 패치는 켜 두되 매번 손대지 않는다(GearRuntime.Active).
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var vanilla = Gravity.Gear.GearItemsBuilder.VanillaIds(sptData);
            Gravity.Gear.GearRuntime.Items = Gravity.Gear.GearItemsBuilder.Build(templates, bots, vanilla);
            Gravity.Gear.GearRuntime.ExpTable = Gravity.Gear.GearItemsBuilder.ExpTable(globals);
            Gravity.Gear.GearRuntime.Activity = activity;
            Gravity.Gear.GearRuntime.Locale = lang => locales.GetLocaleDb(lang);
            new Gravity.Gear.GearLevelPatch().Enable();
            new Gravity.Gear.GearFilterPatch().Enable();
            new Gravity.Gear.GearPlatePatch().Enable();
            new Gravity.Gear.GearBotPatch().Enable();
            new Gravity.Gear.GearDynamicModPatch().Enable();
            new Gravity.Gear.GearDynamicGearPatch().Enable();
            new Gravity.Gear.GearLootPatch().Enable();
            new Gravity.Gear.GearUnheardPatch().Enable();
            var gi = Gravity.Gear.GearRuntime.Items;
            logger.Info($"{GravityInfo.Tag} 장비 자료: 아이템 {gi.Info.Count}개, 장비 칸 {gi.SlotTier.Count}개, 모드 아이템 무기 {gi.ModWeapons.Count}·장비 {gi.ModEquipment.Count}·부품 {gi.ModAttachments.Count}"
                + (vanilla == null ? " (SPT 원본 목록 없음: 모드 구분 끔)" : "") + $", {sw.ElapsedMilliseconds}ms" + (detect.GearLocked ? " / APBS 감지로 장비 모듈 잠금" : ""));
        }
        catch (Exception ex) { logger.Warning($"{GravityInfo.Tag} 장비 모듈 준비 실패, 장비는 SPT 기본: {ex.Message}"); }

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
        if (detect.Ruaf || detect.BlackDiv)
            logger.Info($"{GravityInfo.Tag} 팩션 자료: RUAF 맵 {fd.Ruaf.Count}개, BlackDiv 맵 {fd.BlackDivMaps.Count}개, 연구소 게이트 원본 {fd.LabGateVanilla.Values.Sum(v => v.Count)}개, PMC 지문 맵 {fd.SptPmc.Count}개");
        return Task.CompletedTask;
    }
}

/// <summary>웹 리스너가 쓸 경로(로더가 채움).</summary>
public static class GravityWebPaths
{
    public static GravityPaths? Paths { get; set; }
}
