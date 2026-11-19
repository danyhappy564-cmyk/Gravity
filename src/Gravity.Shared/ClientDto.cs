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
        double[]? Curve = null, double CurveRaidSec = 0, double CurveOffsetSec = 0,
        ScavWaveDto[]? List = null, double ListJitterSec = 0, double LeanWave = 0, double LeanBots = 0);
    // 1.0.8 LeanWave·LeanBots: 스캐브 웨이브 몰림(보충 빈도)·인원 몰림(살아 있는 스캐브 상한), -100 앞 ~ 100 뒤. 0 또는 옛 서버 = 1.0.7과 같음.
    // 1.0.6 스캐브 웨이브 목록: List = 원래 레이드 기준 초·인원 범위. 있으면 새 클라는 목록대로만 낸다(채우기 안 함).
    // 서버는 이때 Style을 "start"로 보내 옛 클라는 시작 스캐브만 내고 채우지 않는다.

    /// <summary>스캐브 웨이브 목록 한 칸(원래 레이드 시작 뒤 초, 인원 최소~최대).</summary>
    public sealed record ScavWaveDto(double TimeSec, int Min, int Max);
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
        bool UseDefault = true, double SightIgnore = 0, double BotDelay = 0, string CustomDiff = "normal");
    // 1.1.0: Sight = 보이는 지점을 피함(시야 검사). SightIgnore = 시야 안 스폰을 허용했을 때 자리마다 시야 검사를 건너뛸 확률(0~1).
    // BotDelay = 한 차례 자리 사이 간격(초, 0 = 한꺼번에). CustomDiff = 직접 고른 종류의 난이도(easy·normal·hard·impossible·random). 옛 서버 = 1.0.9와 같음.

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
        IReadOnlyList<int>? StartGroups = null, bool StartPlayerOnly = false, string? WavePoints = null, int TotalCap = 0,
        int RespawnPmcLeft = -1, int RespawnScavLeft = -1,
        int PerFrame = 0, IReadOnlyList<PreloadDto>? Preload = null, bool PmcAnywhere = false, DevDto? Dev = null, IReadOnlyList<int>? PlannedTimes = null);
    // 1.1.0 클라이언트 화면: PerFrame = Gravity 스캐브 요청을 한 프레임에 이만큼까지(0 = 제한 없음, 옛 서버),
    // Preload = 레이드 시작 때 게임 예비 프로필 목표(BotSpawner.AddToTargetBackup)로 미리 만들어 둘 역할·난이도·수,
    // PmcAnywhere = 시작 분대까지 맵 전체(저격 제외) 지점 사용, Dev = 게임 화면 표시, PlannedTimes = 계획 PMC 분대 시각(초, 화면 표시용).

    public sealed record PreloadDto(string Role, string Difficulty, int Count);

    public sealed record DevDto(bool Markers, bool Zones, bool Overlay);

    /// <summary>1.1.0 '지금 넣어 보기' 명령. Kind: wave·scav·boss·faction·near. Escorts = 대원 수.</summary>
    public sealed record DevCommand(string Kind, string Role, string Escort, int Escorts, string Difficulty, int Id);

    public sealed record DevPollResponse(IReadOnlyList<DevCommand> Commands);
    // 1.1.0 RespawnPmcLeft·RespawnScavLeft: 시작 봇 뒤 Gravity가 다시 넣는 PMC·스캐브 최대 인원(-1 = 제한 없음, 옛 서버도 -1).
    // 1.0.8 TotalCap: 레이드 전체 봇 누적 상한(0 = 끔, 옛 서버도 0). Gravity 스폰만 멈춘다(INBOX #61).
    // 1.0.8 WavePoints: null·"pmc" = 1.0.7과 같음, "spread" = 웨이브 PMC 분대를 맵 전체 봇 구역(저격 제외)에 고르게(INBOX #60).

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
        IReadOnlyDictionary<string, int>? Extracted = null, int ExtractRefill = 0, IReadOnlyDictionary<string, int>? Diag = null);
    // 1.0.8 Diag: overlap·lateEscort·startPlanned·startActive·startDropped(RaidContext.Diag). 옛 클라는 null, 옛 서버는 모르는 칸이라 무시.
}
