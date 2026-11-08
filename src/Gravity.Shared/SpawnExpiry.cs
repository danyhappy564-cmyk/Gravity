namespace Gravity.Shared
{
    /// <summary>
    /// Gravity PMC 분대의 대기열 만료 판정. 바닐라 보스 대기열은 시간 초과가 없어서(3초마다 영원히 재시도)
    /// 첫 실패에서 maxWaitSec(기본 3분)이 지났거나 레이드가 하드 스톱(초)에 닿으면 버린다.
    /// </summary>
    public static class SpawnExpiry
    {
        public static bool Expired(float firstFailTime, float now, float raidElapsed, int hardStopSec, float maxWaitSec = 180) =>
            now - firstFailTime >= maxWaitSec || raidElapsed >= hardStopSec;
    }
}
