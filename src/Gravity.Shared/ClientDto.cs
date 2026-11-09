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
        double RefillSec, int Pending, int SoftCap, int PerPlayer, double PscavChance, double StartDelaySec = 0);

    public sealed record InfDto(double DelaySec, int Batch, double PmcShare, double Jitter);

    public sealed record RefillGroup(bool Usec, int Size, string Difficulty);

    public sealed record NearDto(
        bool On, double FirstDelay, double Interval, double MinDist, double MaxDist, double Spacing,
        int WaveMin, int WaveMax, double SlotChance, double PmcChance, int Attempts,
        bool Sight, bool StopAtCap, bool GroupScavOff, double GroupChance, int GroupMin, int GroupMax,
        bool CustomOn, string CustomT1, int CustomCap1, string CustomT2, int CustomCap2, double CustomRatio2);

    public sealed record ClientRaidSettings(
        string MapId, bool Managed, int Seed, int RaidSec, int HardStopPmcSec, int HardStopScavSec, int Cap,
        double PmcDist, double ScavDist, double SpacingPlayer, double SpacingBots,
        bool ProtectOn, double ProtectSec, double ProtectRadius,
        string Placement, double HotspotShare, IReadOnlyDictionary<string, double> ZoneWeights,
        bool AllowPmcLabyrinth, GroupCapDto GroupCap, ScavDto Scav, InfDto? Inf,
        IReadOnlyList<RefillGroup> RefillGroups, NearDto Near);

    /// <summary>Spawned 키: pmcUSEC·pmcBEAR·assault·near·other. Dropped 키: noPoint·expired·groupCap·cap(하드 스톱으로 버린 PMC 분대)·scavCap(상한에 막힌 스캐브 차례 구간)·nearCap(상한에 막힌 근처 자리).
    /// AlivePerMinute 원소: [분, PMC, 스캐브, 전체].</summary>
    public sealed record RaidReport(
        string MapId, int Seed,
        IReadOnlyDictionary<string, int> Spawned, IReadOnlyDictionary<string, int> Dropped,
        int Sanitized, IReadOnlyList<int[]> AlivePerMinute);
}
