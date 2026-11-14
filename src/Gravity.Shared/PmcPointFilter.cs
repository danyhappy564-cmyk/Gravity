namespace Gravity.Shared
{
    /// <summary>
    /// PMC 분대 후보 지점 거르기(클라 PmcPointsPatch.Pick). 게임은 보스 웨이브 PMC를 EPlayerSide.Savage로 만들고(BotBossSpawn.cs:278)
    /// 플레이어 시작 지점은 Sides=Pmc라 게임 진영 검사(IsValid(side))에 늘 걸린다. Gravity가 이동망을 이어 준 플레이어 지점은
    /// 진영 검사를 건너뛴다(재사용 대기는 그대로). 1.0.6 로컬 판에서 찾음: 이 검사 때문에 "시작 PMC는 플레이어 시작 지점만"이면 시작 PMC가 0명.
    /// </summary>
    public static class PmcPointFilter
    {
        public static bool Usable(bool timeOk, bool sideOk, bool playerPoint, bool onlyPlayer)
            => timeOk && (!onlyPlayer || playerPoint) && (sideOk || playerPoint);
    }
}
