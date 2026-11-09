using System.Reflection;
using Gravity.Core;
using HarmonyLib;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Location;
using SPTarkov.Server.Core.Services.Profile;
using SPTarkov.Server.Core.Services.InRaid;

namespace Gravity.Spawn;

/// <summary>
/// 레이드 시작 때 SPT가 만든 맵 복제본(클라이언트로 갈 것)을 고친다.
/// 앞: 이번 레이드의 시간 조정(스캐브 레이드 시작 지연)을 잡아 둔다 — 원래 함수가 끝나면 지워지기 때문.
/// </summary>
public class RaidStartPatch : AbstractPatch
{
    [ThreadStatic] private static RaidChanges? _pending;
    [ThreadStatic] private static string? _session;
    [ThreadStatic] private static bool _scavRaid;
    internal static ProfileActivityService? Activity;

    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(LocationLifecycleService), nameof(LocationLifecycleService.GenerateLocationAndLoot));

    [PatchPrefix]
    public static void Prefix(MongoId sessionId)
    {
        _session = sessionId.ToString();
        try
        {
            var data = Activity?.GetProfileActivityRaidData(sessionId);
            _pending = data?.RaidAdjustments;
            _scavRaid = data?.RaidConfiguration?.Side == SideType.Savage;
        }
        catch { _pending = null; _scavRaid = false; }
    }

    [PatchPostfix]
    public static void Postfix(string name, bool generateLoot, LocationBase __result)
    {
        var changes = _pending;
        _pending = null;
        var session = _session;
        _session = null;
        if (!generateLoot || __result is null || string.Equals(name, "hideout", StringComparison.OrdinalIgnoreCase))
        {
            // 손대지 않는 판에 지난 판의 클라 설정이 남지 않게 지운다(클라는 Unmanaged를 받는다).
            if (name is not null) GravityRuntime.Instance?.LastClientSettings.TryRemove(name.ToLowerInvariant(), out _);
            return;
        }
        var scavRaid = _scavRaid;
        _scavRaid = false;
        GravityRuntime.Instance?.TryApplyToRaid(name, __result, changes, session, scavRaid: scavRaid);
    }
}
