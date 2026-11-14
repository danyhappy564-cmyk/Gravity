using System.Collections.Generic;

namespace Gravity.Shared
{
    public sealed record HelloRequest(string ClientVersion, bool AbpsClient);

    public sealed record HelloResponse(string ServerVersion, bool Compatible, bool SpawnLocked);

    /// <summary>Cmp는 "eq" 또는 "gte"(0.1 GroupCapRule과 같은 값).</summary>
    public sealed record GroupCapRuleDto(int Size, string Cmp, int Count);

    public sealed record GroupCapDto(bool On, string Scope, string Over, IReadOnlyList<GroupCapRuleDto> Rules);

    public sealed record ScavDto(
        bool Managed, double ActiveMin, double ActiveMax, double QuietMin, double QuietMax,
        double RefillSec, int Pending, int SoftCap, int PerPlayer, double PscavChance, double StartDelaySec = 0, string Style = "waves", double? FirstRefillSec = null, int InitialCount = 0,
        double[]? Curve = null, double CurveRaidSec = 0, double CurveOffsetSec = 0);
    // 1.0.6: Curve = 레이드 초반·중반·후반 스캐브 강도(null = 옛 동작). 3등분 기준은 원래 레이드 길이 CurveRaidSec,
    // 클라 경과 초 + CurveOffsetSec(스캐브 레이드 지연)으로 지금 칸을 정한다. 옛 클라는 이 칸을 모르고 무시한다.

    /// <summary>FirstDelaySec(1.0): PMC 첫 보충 시각(클라 경과 초, 스캐브 레이드 지연을 뺀 값). 옛 서버는 null — 이때는 예전 동작.</summary>
    public sealed record InfDto(double DelaySec, int Batch, double PmcShare, double Jitter, double? FirstDelaySec = null);

    public sealed record RefillGroup(bool Usec, int Size, string Difficulty);

    public sealed record NearDto(
        bool On, double FirstDelay, double Interval, double MinDist, double MaxDist, double Spacing,
        int WaveMin, int WaveMax, double SlotChance, double PmcChance, int Attempts,
        bool Sight, bool StopAtCap, bool GroupScavOff, double GroupChance, int GroupMin, int GroupMax,
        bool CustomOn, string CustomT1, int CustomCap1, string CustomT2, int CustomCap2, double CustomRatio2,
        bool UseDefault = true);

    public sealed record ClientRaidSettings(
        string MapId, bool Managed, int Seed, int RaidSec, int HardStopPmcSec, int HardStopScavSec, int Cap,
        double PmcDist, double ScavDist, double SpacingPlayer, double SpacingBots,
        bool ProtectOn, double ProtectSec, double ProtectRadius,
        string Placement, double HotspotShare, IReadOnlyDictionary<string, double> ZoneWeights,
        bool AllowPmcLabyrinth, GroupCapDto GroupCap, ScavDto Scav, InfDto? Inf,
        IReadOnlyList<RefillGroup> RefillGroups, NearDto Near, IReadOnlyList<string>? CapExclude = null,
        bool RefillAfterExtract = false, string LogLevel = "summary", IReadOnlyDictionary<string, double>? ScavZoneWeights = null,
        string? ExtractRefillMode = null, int? ExtractRefillCap = null, IReadOnlyList<string>? DisabledZones = null,
        IReadOnlyList<int>? PlannedGroups = null, string? WaveMark = null, CapTableDto? Caps = null,
        IReadOnlyList<int>? StartGroups = null, bool StartPlayerOnly = false);

    /// <summary>
    /// 종류별 상한 표(1.0, 사용자 11-10). Kinds 키: boss·special·faction·event(CapKinds). 방식(CapKinds.Separate 등):
    /// separate = 상한 무시 + 맵 상한 계산에서 뺌, vanilla = 원본 그대로(나오면 자리 차지), respect = 상한 지킴. Max 0 = 제한 없음.
    /// PmcReserve = 스캐브·근처 보충이 비워 둘 PMC 자리, PmcGroups = 동시에 살아 있을 PMC 무리 최대(0 = 제한 없음).
    /// FactionRoles = 팩션 종류로 셀 모드 역할(이름 또는 번호).
    /// </summary>
    public sealed record CapTableDto(int PmcReserve, int PmcGroups, IReadOnlyDictionary<string, CapKindDto> Kinds,
        IReadOnlyList<string>? FactionRoles = null);

    public sealed record CapKindDto(string Mode, int Max);

    /// <summary>Spawned 키: pmcUSEC·pmcBEAR·assault·near·other. Dropped 키: noPoint·expired·groupCap·cap(하드 스톱으로 버린 PMC 분대)·scavCap(상한에 막힌 스캐브 차례 구간)·nearCap(상한에 막힌 근처 자리).
    /// AlivePerMinute 원소: [분, PMC, 스캐브, 전체]. Extracted(0.6): 살아서 맵을 떠난 봇(탈출) 수, 키는 Killed와 같다.</summary>
    public sealed record RaidReport(
        string MapId, int Seed,
        IReadOnlyDictionary<string, int> Spawned, IReadOnlyDictionary<string, int> Dropped,
        int Sanitized, IReadOnlyList<int[]> AlivePerMinute, IReadOnlyDictionary<string, int>? Killed = null,
        IReadOnlyDictionary<string, int>? Extracted = null, int ExtractRefill = 0);
}
