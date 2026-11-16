using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Gravity.Shared
{
    /// <summary>늦게 나온 대원 판정 결과(1.0.8, 제보 ④).</summary>
    public enum EscortVerdict { Keep, DropLeaderDead, DropNearHuman }

    /// <summary>
    /// 1.0.8 분대 겹침·늦은 대원 규칙(INBOX #59). 게임 쪽 값(시각·거리·생존)은 클라가 넣고 여기서는 판정만 한다.
    /// </summary>
    public static class SquadRules
    {
        /// <summary>분대장이 이 거리 안에서 다른 PMC 무리 옆에 나오면 "겹침 생성"으로 센다(보고용).</summary>
        public const double OverlapDist = 30;

        /// <summary>대원이 분대장 활성화보다 이만큼 늦으면 "늦은 대원"으로 센다(보고용).</summary>
        public const double LateReportSec = 60;

        /// <summary>대원이 분대장보다 이만큼 늦었을 때부터 버림 판정을 한다. 바로 따라 나오는 대원(보통 1초 안)은 건드리지 않는다.</summary>
        public const double LateJudgeSec = 30;

        /// <summary>생성 중 분대 지점을 다른 분대 후보에서 빼는 최대 시간. 분대장이 실패하면 게임이 목록에서 안 빼므로 상한을 둔다.
        /// 1.0 3차 점검에서 레이드 중 불린 분대는 몸 읽기로 2~4분 늦게 나왔다.</summary>
        public const double InFlightMaxSec = 240;

        public static bool Overlaps(double nearestOtherPmc) => nearestOtherPmc < OverlapDist;

        public static bool IsLate(double lateSec) => lateSec >= LateReportSec;

        /// <summary>
        /// 대원 몸이 놓이는 순간의 판정. 분대장보다 LateJudgeSec 이상 늦었고, 분대장이 이미 죽었거나 그 자리가 사람에게서
        /// minHumanDist(분대장 지점을 고를 때 쓰는 완화된 PMC 거리) 안이면 버린다.
        /// </summary>
        public static EscortVerdict JudgeEscort(double lateSec, bool leaderAlive, double humanDist, double minHumanDist)
        {
            if (lateSec < LateJudgeSec) return EscortVerdict.Keep;
            if (!leaderAlive) return EscortVerdict.DropLeaderDead;
            if (humanDist < minHumanDist) return EscortVerdict.DropNearHuman;
            return EscortVerdict.Keep;
        }
    }

    /// <summary>
    /// 생성 중 분대 지점(게임 BotBossSpawn._spawnProcess 항목). 게임은 분대장이 Active가 되면 항목을 빼지만 실패하면 남겨 두므로,
    /// 처음 본 시각에서 maxSec이 지난 항목은 더 쓰지 않는다. 항목 객체가 사라지면 기록도 같이 사라진다.
    /// </summary>
    public sealed class InFlightPoints
    {
        private sealed class Seen { public double At; }
        private readonly ConditionalWeakTable<object, Seen> _seen = new ConditionalWeakTable<object, Seen>();

        /// <summary>entry가 아직 유효하면 pos를 into에 넣고 true.</summary>
        public bool Add(object entry, Vec3 pos, double now, double maxSec, List<Vec3> into)
        {
            var s = _seen.GetValue(entry, _ => new Seen { At = now });
            if (now - s.At >= maxSec) return false;
            into.Add(pos);
            return true;
        }
    }
}
