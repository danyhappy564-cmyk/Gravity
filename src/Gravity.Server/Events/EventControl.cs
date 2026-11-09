using System.Text.Json.Nodes;
using Gravity.Core;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;

namespace Gravity.Events;

/// <summary>SVM 프리셋의 이벤트 칸(Raids.RaidEvents).</summary>
public sealed record SvmEvents(bool DisableEvents, bool Christmas, bool Halloween)
{
    public bool TouchesEvents => DisableEvents || Christmas || Halloween;
}

/// <summary>
/// 0.3 시즌 이벤트 켜고 끄기. SPT는 GameCallbacks 단계(PostDbLoadService)에서 날짜를 보고 이벤트를 DB에 한 번 적용한다.
/// Gravity는 그 직전에 SPT 이벤트 설정(SeasonalEventConfig)만 고친다. DB는 건드리지 않는다.
/// </summary>
public static class EventControl
{
    /// <summary>웹 eventsOwner → 실제 담당(gravity·spt·svm·ppes).</summary>
    public static string ResolveOwner(string owner, bool ppes, SvmEvents? svm) => owner switch
    {
        "auto" => ppes ? "ppes" : svm is { TouchesEvents: true } ? "svm" : "gravity",
        _ => owner
    };

    /// <summary>담당이 Gravity일 때 SPT 이벤트 설정을 고친다. 바꾼 내용을 사람이 읽을 줄로 돌려준다.</summary>
    public static List<string> Apply(SeasonalEventConfig cfg, EventSettings ev)
    {
        var log = new List<string>();
        var modes = new Dictionary<SeasonalEventType, string>
        {
            [SeasonalEventType.Halloween] = ev.Halloween,
            [SeasonalEventType.Christmas] = ev.Christmas,
            [SeasonalEventType.NewYears] = ev.NewYears,
            [SeasonalEventType.AprilFools] = ev.AprilFools
        };
        bool anyOn = modes.Values.Any(m => m == "on");
        cfg.EnableSeasonalEventDetection = ev.Detect || anyOn;
        log.Add(L.T($"감지 {(cfg.EnableSeasonalEventDetection ? "켬" : "끔")}", $"detection {(cfg.EnableSeasonalEventDetection ? "on" : "off")}"));

        foreach (var e in cfg.Events)
        {
            if (!modes.TryGetValue(e.Type, out var mode)) continue;
            if (mode == "off") e.Enabled = false;
            else if (mode == "on")
            {
                e.Enabled = true;
                e.StartMonth = 1; e.StartDay = 1; e.EndMonth = 12; e.EndDay = 31;
            }
            var st = e.Settings;
            if (st != null && e.Type == SeasonalEventType.Halloween)
            {
                st.EnableSummoning = ev.Summon;
                if (st.ZombieSettings != null) st.ZombieSettings.Enabled = ev.Zombies > 0;
            }
            if (st != null && e.Type == SeasonalEventType.Christmas)
            {
                st.EnableSanta = ev.Santa > 0;
                st.EnableChristmasHideout = ev.Gifts;
                st.AdjustBotAppearances = ev.Snow;
            }
            log.Add($"{e.Name} {mode}{(e.Enabled ? "" : L.T("(꺼짐)", " (off)"))}");
        }

        if (!ev.HalloweenGear && cfg.EventGear.Remove(SeasonalEventType.Halloween)) log.Add(L.T("할로윈 이벤트 장비 끔", "Halloween event gear off"));
        if (!ev.ChristmasGear && cfg.EventGear.Remove(SeasonalEventType.Christmas)) log.Add(L.T("크리스마스 이벤트 장비 끔", "Christmas event gear off"));
        return log;
    }

    /// <summary>SVM 폴더의 Loader/loader.json이 고른 프리셋에서 Raids.RaidEvents를 읽는다. 못 읽으면 null.</summary>
    public static SvmEvents? ReadSvm(string? svmDir)
    {
        try
        {
            if (svmDir == null) return null;
            var loader = JsonNode.Parse(File.ReadAllText(Path.Combine(svmDir, "Loader", "loader.json")));
            var preset = loader?["CurrentlySelectedPreset"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(preset) || preset.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            var p = JsonNode.Parse(File.ReadAllText(Path.Combine(svmDir, "Presets", preset + ".json")));
            var re = p?["Raids"]?["RaidEvents"];
            if (re == null) return null;
            bool B(string k) => re[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
            return new SvmEvents(B("DisableEvents"), B("Christmas"), B("Halloween"));
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>SPT가 시즌 이벤트를 적용하기(GameCallbacks) 직전에 한 번.</summary>
[Injectable(TypePriority = OnLoadOrder.GameCallbacks - 1)]
public class GravityEventLoader(ISptLogger<GravityEventLoader> logger, ModHelper modHelper, SeasonalEventConfig seasonal,
    SPTarkov.Server.Core.Services.Locales.LocaleService locales) : IOnLoad
{
    /// <summary>웹 진단용: 실제 담당과 SVM 이벤트 칸.</summary>
    public static string? ResolvedOwner { get; private set; }
    public static SvmEvents? Svm { get; private set; }
    public static List<string> Applied { get; } = [];

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // 이 로더가 GravityLoader보다 먼저 돈다: 콘솔 언어를 여기서도 정한다(사용자 11-10, 영어 서버에서 이 줄만 한글이던 것)
        try { L.Use(locales.GetDesiredServerLocale()); } catch { /* 윈도우 언어 그대로 */ }
        try
        {
            GravityBoot.Ensure(modHelper);
            var det = GravityBoot.Detect!;
            if (det.SpawnLocked) return Task.CompletedTask;
            var ev = GravityBoot.Store!.Current.Events ?? ConfigModel.Default().Events!;
            Svm = det.SvmFolder == null ? null : EventControl.ReadSvm(Path.Combine(GravityBoot.ModsDir!, det.SvmFolder));
            ResolvedOwner = EventControl.ResolveOwner(ev.Owner, det.Ppes, Svm);
            if (ResolvedOwner != "gravity")
            {
                logger.Info($"{GravityInfo.Tag} {L.T("시즌 이벤트 담당", "Seasonal events handled by")}: {ResolvedOwner} {L.T("(Gravity는 이벤트 봇 확률만)", "(Gravity only sets event bot chances)")}");
                return Task.CompletedTask;
            }
            Applied.AddRange(EventControl.Apply(seasonal, ev));
            logger.Info($"{GravityInfo.Tag} {L.T("시즌 이벤트 담당", "Seasonal events handled by")}: Gravity — {string.Join(", ", Applied)}");
        }
        catch (Exception ex)
        {
            logger.Warning($"{GravityInfo.Tag} {L.T("시즌 이벤트 설정 실패(SPT 기본 그대로)", "Seasonal event setup failed (SPT defaults kept)")}: {ex.Message}");
        }
        return Task.CompletedTask;
    }
}
