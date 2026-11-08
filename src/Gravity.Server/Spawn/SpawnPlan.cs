namespace Gravity.Spawn;

/// <summary>맵 기본 정보(서버 로드 때 DB에서 모음). BasePmc·BaseScav는 바닐라 레이드 한 판 기준 인원.</summary>
public sealed record MapInfo(string Id, int BasePmc, int BaseScav, int DbRaidSec, string[] PmcZones, string[] ScavZones);

public sealed record PmcGroup(int TimeSec, bool Usec, int Size, string Difficulty, bool IsStart);

public sealed record ScavWave(int TimeMinSec, int TimeMaxSec, int SlotsMin, int SlotsMax);

public sealed record SpawnPlan(
    string MapId, int RaidSec, int SimStartSec, int Seed, string Style,
    List<PmcGroup> Groups, List<ScavWave> ScavWaves, int BotCap, int DroppedPmcs)
{
    public int PmcTotal => Groups.Sum(g => g.Size);
    public int StartPmcs => Groups.Where(g => g.IsStart).Sum(g => g.Size);

    public string Summary =>
        $"{MapId} 레이드 {RaidSec / 60}분(시작 {SimStartSec}초) 방식 {Style} PMC 시작 {StartPmcs}({Groups.Count(g => g.IsStart)}그룹)"
        + $"+웨이브 {Groups.Count(g => !g.IsStart)}그룹({PmcTotal - StartPmcs}명)"
        + (DroppedPmcs > 0 ? $" 제한으로 뺀 {DroppedPmcs}명" : "")
        + (ScavWaves.Count > 0 ? $" 스캐브 웨이브 {ScavWaves.Count}개({ScavWaves.Sum(w => w.SlotsMax)}명 이하)" : " 스캐브 바닐라 유지")
        + $" 시드 {Seed}";
}
