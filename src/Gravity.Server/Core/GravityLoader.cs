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
    TemplateTable templates, BotTable bots, GlobalTable globals, SPTarkov.Server.Core.Services.Locales.LocaleService locales,
    SPTarkov.Server.Core.Utils.Cloners.ICloner cloner) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try { L.Use(locales.GetDesiredServerLocale()); } catch { /* 윈도우 언어 그대로 */ }
        GravityBoot.Ensure(modHelper);
        var paths = GravityBoot.Paths!;
        GravityWebPaths.Paths = paths;
        var store = GravityBoot.Store!;
        var load = GravityBoot.Load!;
        if (!load.Ok) logger.Warning($"{GravityInfo.Tag} {load.Error}");
        var detect = GravityBoot.Detect!;

        var catalog = new MapCatalog();
        catalog.Build(locations, pmcConfig, botConfig);

        // 0.6 진단: 로그 수준(diag.log)은 저장된 설정을 그때그때 읽는다. 경고·알림은 최근 목록(웹)에도 남긴다.
        DiagLog.Level = () => store.Current.DiagOrDefault.Log;
        DiagLog.ReportsDir = paths.ReportsDir;
        var library = new PresetLibrary(paths.PresetsFile, paths.BackupDir);
        library.Load();
        if (library.LoadWarning != null) logger.Warning($"{GravityInfo.Tag} {library.LoadWarning}");
        var orbitVer = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name is "Orbit.Server" or "ORBIT.Server")?.GetName().Version?.ToString(3);
        string? orbitJson = null;
        if (detect.Orbit && detect.OrbitFolder != null)
            try
            {
                var f = Path.Combine(GravityBoot.ModsDir!, detect.OrbitFolder, "config.json");
                if (File.Exists(f)) orbitJson = File.ReadAllText(f);
            }
            catch (Exception ex) { logger.Warning($"{GravityInfo.Tag} {L.T("ORBIT 설정을 못 읽음(기본값으로 맞춤)", "Could not read the ORBIT config (using defaults)")}: {ex.Message}"); }
        var orbit = OrbitInfo.Parse(detect.Orbit, orbitVer, orbitJson);

        GravityRuntime.Instance = new GravityRuntime(store, catalog, detect, DiagLog.Warn(m => logger.Warning(m)), DiagLog.Info(m => logger.Info(m)))
        {
            Orbit = orbit,
            Library = library,
            Verbose = DiagLog.Verbose(m => logger.Info(m)),
            CurrentBase = id => locations.GetLocation(id)?.Base,
            CloneBase = id => locations.GetLocation(id)?.Base is { } b ? cloner.Clone(b) : null,
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
        var active = catalog.DiscoverVariants(detect,
            id =>
            {
                if (sptData == null) return null;
                var f = Path.Combine(sptData, "database", "locations", id, "base.json");
                return File.Exists(f) ? File.ReadAllText(f) : null;
            },
            raw => json.Deserialize<SPTarkov.Server.Core.Models.Eft.Common.LocationBase>(raw));
        GravityRuntime.Instance.ActiveVariants = active;
        // 1.0: SVM 추가 레이드 시간(사용 중인 프리셋)을 웹 맵 정보에 반영
        var svmAdd = detect.SvmFolder == null ? 0 : Gravity.Events.EventControl.ReadSvmRaidMinutes(Path.Combine(GravityBoot.ModsDir!, detect.SvmFolder));
        if (svmAdd > 0)
        {
            catalog.ApplySvmRaidTime(svmAdd, id =>
            {
                if (sptData == null) return null;
                var f = Path.Combine(sptData, "database", "locations", id, "base.json");
                if (!File.Exists(f)) return null;
                var b = json.Deserialize<SPTarkov.Server.Core.Models.Eft.Common.LocationBase>(File.ReadAllText(f));
                return b?.EscapeTimeLimit is { } e ? (int)Math.Round(e) : null;
            });
            logger.Info($"{GravityInfo.Tag} " + L.T($"SVM 추가 레이드 시간 +{svmAdd}분 반영", $"SVM extra raid time +{svmAdd} min applied"));
        }
        if (catalog.AutomaticVariants.Count > 0)
            logger.Info($"{GravityInfo.Tag} {L.T("자동 감지된 변형 맵", "Auto-detected map variants")}: {string.Join(", ", catalog.AutomaticVariants)}");
        foreach (var m in catalog.All.Where(m => m.HasVariant && m.OriginalPmcZones == null))
            logger.Warning($"{GravityInfo.Tag} {m.Id}: {L.T("SPT 원본 구역을 못 읽어 웹의 원본 배치 편집을 비활성화합니다", "could not read the SPT original zones; original-layout zone editing is disabled in the web UI")}");
        if (sptData == null) logger.Warning($"{GravityInfo.Tag} {L.T("SPT_Data 폴더를 못 찾아 다른 모드 PMC 구분·연구소 게이트 복원을 끕니다", "SPT_Data folder not found; other-mod PMC detection and Labs gate restore are off")}");
        RaidStartPatch.Activity = activity;
        new RaidStartPatch().Enable();

        // 0.5 장비: 아이템 정보·자동 티어(읽기만)와 봇 생성 패치. APBS가 있으면 패치를 걸지 않는다(GearRuntime.Active도 잠금).
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var vanilla = Gravity.Gear.GearItemsBuilder.VanillaIds(sptData);
            Gravity.Gear.GearRuntime.Builder = () => Gravity.Gear.GearItemsBuilder.Build(templates, bots, vanilla, GravityBoot.ModsDir,
                globals.ItemPresets.Values.Where(x => x.Encyclopedia.HasValue && x.Items != null).Select(x => (x.Encyclopedia!.Value.ToString(), x.Items!.Select(i => i.Template.ToString()))));
            Gravity.Gear.GearRuntime.Items = Gravity.Gear.GearRuntime.Builder();
            Gravity.Gear.GearRuntime.ExpTable = Gravity.Gear.GearItemsBuilder.ExpTable(globals);
            Gravity.Gear.GearRuntime.Activity = activity;
            Gravity.Gear.GearRuntime.Templates = templates.Items;
            Gravity.Gear.GearRuntime.Locale = lang => locales.GetLocaleDb(lang);
            // APBS가 있으면 장비 패치를 아예 걸지 않는다. 걸어만 둬도 Harmony_Profiles(PBT)가 봇 레벨 함수에
            // 다른 모드 패치가 있다고 보고 APBS 커리어 연동을 끈다(1.0.1).
            if (!detect.GearLocked)
            {
                new Gravity.Gear.GearLevelPatch().Enable();
                new Gravity.Gear.GearFilterPatch().Enable();
                new Gravity.Gear.GearPlatePatch().Enable();
                new Gravity.Gear.GearBotPatch().Enable();
                new Gravity.Gear.GearDynamicModPatch().Enable();
                new Gravity.Gear.GearModPoolPatch().Enable();
                new Gravity.Gear.GearItemDbModPatch().Enable();
                new Gravity.Gear.GearDynamicGearPatch().Enable();
                new Gravity.Gear.GearLootPatch().Enable();
                new Gravity.Gear.GearUnheardPatch().Enable();
            }
            var gi = Gravity.Gear.GearRuntime.Items;
            logger.Info($"{GravityInfo.Tag} " + L.T($"장비 자료: 아이템 {gi.Info.Count}개, 장비 칸 {gi.SlotTier.Count}개, 모드 아이템 무기 {gi.ModWeapons.Count}·장비 {gi.ModEquipment.Count}·부품 {gi.ModAttachments.Count}",
                $"Gear data: {gi.Info.Count} items, {gi.SlotTier.Count} gear slots, mod items: {gi.ModWeapons.Count} weapons · {gi.ModEquipment.Count} gear · {gi.ModAttachments.Count} attachments") + $""
                + (vanilla == null ? L.T(" (SPT 원본 목록 없음: 모드 구분 끔)", " (no SPT original list: mod-item split off)") : "") + $", {sw.ElapsedMilliseconds}ms"
                + (detect.GearLocked ? L.T(" / APBS 감지로 장비 모듈 잠금", " / gear module locked (APBS detected)") : ""));
        }
        catch (Exception ex) { logger.Warning($"{GravityInfo.Tag} {L.T("장비 모듈 준비 실패, 장비는 SPT 기본", "Gear module failed to start; gear stays SPT default")}: {ex.Message}"); }

        logger.Info($"{GravityInfo.Tag} {GravityInfo.Version} " + L.T($"로드: 맵 {catalog.All.Count}개, 설정 {(store.RawState == null ? "기본값" : "저장본")}, 웹 /gravity/",
            $"loaded: {catalog.All.Count} maps, settings {(store.RawState == null ? "default" : "saved")}, web /gravity/"));
        var found = new List<string>();
        if (detect.Abps) found.Add(L.T("ABPS(스폰 모듈 잠금)", "ABPS (spawn module locked)"));
        if (detect.Apbs) found.Add("APBS");
        if (detect.Svm) found.Add("SVM");
        if (detect.Orbit) found.Add(orbit.ConfigRead
            ? $"ORBIT{(orbit.Version != null ? " " + orbit.Version : "")}" + (orbit.Limiter
                ? L.T($"(봇 재우기 켬, 깨우는 거리 {orbit.WakeDistance:0}m)", $"(bot sleeping on, wake distance {orbit.WakeDistance:0}m)")
                : L.T("(봇 재우기 끔)", "(bot sleeping off)"))
            : L.T("ORBIT(설정 파일 못 읽음)", "ORBIT (config not readable)"));
        if (detect.Sain) found.Add("SAIN");
        if (detect.Ruaf) found.Add("RUAF");
        if (detect.Untar) found.Add("UNTAR");
        if (detect.BlackDiv) found.Add("BlackDiv");
        if (detect.QuestingBots)
        {
            found.Add(detect.SpawnLocked ? L.T("QuestingBots(설정에 따라 스폰 잠금)", "QuestingBots (spawns locked by setting)") : L.T("QuestingBots(중복 스폰 주의)", "QuestingBots (watch for double spawns)"));
            DiagLog.Warn(m => logger.Warning(m))($"{GravityInfo.Tag} {L.T("Questing Bots의 PMC/플레이어 스캐브 스폰을 끄지 않으면 봇이 두 배로 나올 수 있습니다", "Turn off Questing Bots' PMC/player-scav spawning or bots may spawn twice")}");
        }
        if (detect.Blackout) found.Add("Blackout");
        if (detect.MapVariants) found.Add("MapVariants");
        if (detect.VariantMaps.Count > 0) found.Add(L.T("변형 맵 ", "map variants ") + string.Join(", ", detect.VariantMaps.Select(kv => $"{kv.Key}({string.Join("·", kv.Value)})")));
        if (found.Count > 0) { logger.Info($"{GravityInfo.Tag} {L.T("감지", "Detected")}: {string.Join(", ", found)}"); DiagLog.Remember("INFO", L.T("감지: ", "Detected: ") + string.Join(", ", found)); }
        if (detect.Ruaf || detect.BlackDiv)
            logger.Info($"{GravityInfo.Tag} " + L.T($"팩션 자료: RUAF 맵 {fd.Ruaf.Count}개, BlackDiv 맵 {fd.BlackDivMaps.Count}개, 연구소 게이트 원본 {fd.LabGateVanilla.Values.Sum(v => v.Count)}개, PMC 지문 맵 {fd.SptPmc.Count}개",
                $"Faction data: RUAF {fd.Ruaf.Count} maps, BlackDiv {fd.BlackDivMaps.Count} maps, {fd.LabGateVanilla.Values.Sum(v => v.Count)} original Labs gate entries, PMC fingerprints {fd.SptPmc.Count} maps"));
        return Task.CompletedTask;
    }
}

/// <summary>웹 리스너가 쓸 경로(로더가 채움).</summary>
public static class GravityWebPaths
{
    public static GravityPaths? Paths { get; set; }
}
