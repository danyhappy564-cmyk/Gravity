using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using EFT.Game.Spawning;
using Gravity.Shared;
using UnityEngine;

namespace Gravity.Client
{
    /// <summary>
    /// 지금 레이드의 서버 설정과 집계. 서버가 관리하는 레이드(S.Managed=true)일 때만 있다.
    /// 설정 받기는 LocalGame.Create 직후 비동기로 시작하고(게임 스레드는 기다리지 않음),
    /// 결과는 Current를 처음 읽을 때(봇 프로필 미리 읽기, BotsController.SetSettings 뒤, GameWorld.OnGameStarted 뒤) 붙인다.
    /// OnGameStarted까지 응답이 없으면 그 레이드는 손대지 않는다. 레이드 끝(GameWorld.Dispose 앞)에서 보고를 보내고 지운다.
    /// </summary>
    public sealed class RaidContext
    {
        private static readonly object Gate = new object();
        private static RaidContext? _current;
        private static Task<ClientRaidSettings?>? _pending;
        private static string _pendingMap = "";
        private static float _pendingStart;

        public ClientRaidSettings S { get; }

        /// <summary>레이드 시작 시각(Time.time). 만들 때 한 번, OnGameStarted 때 다시 맞춘다.</summary>
        public float StartTime { get; internal set; }

        /// <summary>이 레이드에서 프로필 정리로 바꾼 외형·지운 장비 수.</summary>
        public int Sanitized;

        /// <summary>맵 상한을 BotsController에 걸었는지(CapPatch 내부용).</summary>
        internal bool CapApplied;

        /// <summary>구역별 PMC 후보 지점: 맵의 플레이어 지점(Player 카테고리, Infiltration 있음)과 봇 PMC 지점(BotPmc 카테고리)을
        /// BotsController.GetClosestZone으로 가장 가까운 구역에 배정한다. 레이드에서 한 번 만든다(PmcZones.EnsurePoints).</summary>
        public readonly Dictionary<BotZone, List<Candidate>> ZonePoints = new Dictionary<BotZone, List<Candidate>>();

        /// <summary>Candidate.Id → 게임 지점.</summary>
        public readonly Dictionary<int, ISpawnPoint> PointById = new Dictionary<int, ISpawnPoint>();

        /// <summary>방금 다른 분대가 잡은 지점(10초). 지점을 고를 때만 지난 것을 지운다(매 프레임 일 없음).</summary>
        public readonly List<(Vec3 pos, float until)> Reserved = new List<(Vec3 pos, float until)>();

        /// <summary>Gravity 분대(웨이브 항목)별 첫 실패 시각(Time.time). 대기열 만료 판정용.</summary>
        public readonly ConditionalWeakTable<BossLocationSpawn, StrongBox<float>> FirstFail =
            new ConditionalWeakTable<BossLocationSpawn, StrongBox<float>>();

        /// <summary>생성한 인원(키 pmcUSEC·pmcBEAR = PMC 분대 실제 생성, assault = ScavDirector가 요청한 스캐브,
        /// near = NearDirector가 요청한 근처 보충 인원에서 대기열로 가다 버린 수를 뺀 것). 보고용.</summary>
        public readonly Dictionary<string, int> Spawned = new Dictionary<string, int> { ["pmcUSEC"] = 0, ["pmcBEAR"] = 0, ["assault"] = 0, ["near"] = 0 };

        /// <summary>죽은 봇 수(0.3): pmcUSEC·pmcBEAR·assault(assaultGroup 포함)·marksman·boss(보스·호위)·other. BotSpawner.OnBotRemoved(=BotDied)로 센다.</summary>
        public readonly Dictionary<string, int> Killed = new Dictionary<string, int> { ["pmcUSEC"] = 0, ["pmcBEAR"] = 0, ["assault"] = 0, ["marksman"] = 0, ["boss"] = 0, ["other"] = 0 };

        /// <summary>이 레이드의 BotSpawner.OnBotRemoved에 붙었는지(게임 스레드).</summary>
        internal EFT.BotSpawner? DeathHookedTo;

        /// <summary>버린 수(키 noPoint·expired·groupCap·cap). cap = 맵 상한 대기 중 하드 스톱에 닿아 버린 PMC 분대 +
        /// ScavDirector 차례가 상한에 막힌 구간 수(막힌 차례가 이어지는 동안 한 번) + 상한에 막힌 근처 보충 자리 수.
        /// noPoint에는 지점을 못 찾은 근처 보충 자리 수도 들어간다. 보고용.</summary>
        public readonly Dictionary<string, int> Dropped = new Dictionary<string, int> { ["noPoint"] = 0, ["expired"] = 0, ["groupCap"] = 0, ["cap"] = 0, ["scavCap"] = 0, ["nearCap"] = 0 };

        /// <summary>1분마다 기록한 [분, PMC, 스캐브, 전체] (살아 있는 봇 수). 보고용.</summary>
        public readonly List<int[]> AlivePerMinute = new List<int[]>();

        private int _nextSampleMinute;

        /// <summary>
        /// 레이드 시간으로 60초가 지났을 때만 true(분 번호는 minute). 프레임마다 불러도 가볍다: 시각 비교 한 번.
        /// 몇 분을 건너뛰었어도(렉) 한 번만 기록하고 다음 분으로 넘어간다.
        /// </summary>
        internal bool DueForSample(out int minute)
        {
            minute = _nextSampleMinute;
            // 시작 전 Elapsed는 LocalGame.Create 기준이라 분 번호가 밀린다: OnGameStarted 뒤부터만 기록.
            if (!Started) return false;
            if (Elapsed < _nextSampleMinute * 60f) return false;
            _nextSampleMinute = Math.Max(_nextSampleMinute + 1, (int)(Elapsed / 60f) + 1);
            minute = _nextSampleMinute - 1;
            return true;
        }

        /// <summary>지점·구역 섞기용(레이드 시드로 시작). 게임 스레드에서만 쓴다.</summary>
        public readonly System.Random Rng;

        /// <summary>OnGameStarted를 지났는지(게임 스레드). ScavDirector는 이때부터 돈다.</summary>
        internal bool Started;

        /// <summary>ScavDirector 상태(첫 차례에 만든다).</summary>
        internal Directors.ScavDirector.State? ScavState;

        /// <summary>ScavDirector에서 예외가 났으면 true: 이 레이드의 남은 시간은 바닐라 자동 스폰으로 돌린다.</summary>
        internal bool ScavFailed;

        /// <summary>NearDirector 상태(근처 보충이 켜진 레이드의 첫 틱에 만든다).</summary>
        internal Directors.NearDirector.State? NearState;

        /// <summary>NearDirector에서 예외가 났으면 true: 이 레이드의 남은 시간은 근처 보충을 하지 않는다.</summary>
        internal bool NearFailed;

        /// <summary>NearDirector가 넣은 웨이브. PMC 패치(지점·그룹 제한·대기열)가 근처 보충 웨이브를 알아보는 데 쓴다.</summary>
        internal readonly ConditionalWeakTable<BossLocationSpawn, Directors.NearDirector.Tag> NearWaves =
            new ConditionalWeakTable<BossLocationSpawn, Directors.NearDirector.Tag>();

        /// <summary>ZonePoints를 만들었는지 / PMC 구역 0개 맵 보충을 끝냈는지(PmcZones 내부용).</summary>
        internal bool PointsBuilt;
        internal bool ZonesFilled;

        private RaidContext(ClientRaidSettings s, float startTime)
        {
            S = s;
            StartTime = startTime;
            Rng = new System.Random(s.Seed);
        }

        /// <summary>레이드 시작 뒤 지난 초(게임 스레드에서만).</summary>
        public float Elapsed => Time.time - StartTime;

        /// <summary>지금 레이드. 서버 설정을 못 받았거나 Managed=false면 null.</summary>
        public static RaidContext? Current
        {
            get
            {
                if (_pending != null) TryResolve();
                return _current;
            }
        }

        /// <summary>레이드를 만들 때 서버 설정 받기를 시작한다(기다리지 않음).</summary>
        internal static void BeginFetch(string mapId)
        {
            lock (Gate)
            {
                _current = null;
                _pendingMap = mapId ?? "";
                _pendingStart = Time.time; // 게임 스레드에서 부른다. Time.time은 다른 스레드에서 못 읽는다.
                _pending = ClientLink.FetchRaidAsync(_pendingMap);
            }
        }

        /// <summary>받기가 끝났으면 Current를 만든다. 아직이면 false.</summary>
        internal static bool TryResolve()
        {
            lock (Gate)
            {
                var t = _pending;
                if (t == null) return true;
                if (!t.IsCompleted) return false;
                _pending = null;
                var s = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                if (s == null)
                {
                    Log.Warn($"{_pendingMap} 서버 설정을 못 받음: 이 레이드는 바닐라로 진행");
                    return true;
                }
                if (!s.Managed)
                {
                    Log.Info($"{_pendingMap} 서버가 관리하지 않는 레이드: 바닐라로 진행");
                    return true;
                }
                _current = new RaidContext(s, _pendingStart);
                Log.Info($"{s.MapId} 서버 설정 받음: 상한 {s.Cap}, 배치 {s.Placement}");
                return true;
            }
        }

        /// <summary>OnGameStarted 때(게임 스레드): 아직 응답이 없으면 이 레이드는 포기(바닐라).</summary>
        internal static void OnGameStarted()
        {
            lock (Gate)
            {
                if (!TryResolve())
                {
                    _pending = null;
                    Log.Warn($"{_pendingMap} 레이드 시작까지 서버 설정이 오지 않음: 이 레이드는 바닐라로 진행");
                }
                if (_current != null)
                {
                    _current.StartTime = Time.time;
                    _current.Started = true;
                }
            }
        }

        /// <summary>레이드 끝: 보고를 보내고 지운다.</summary>
        internal static void End()
        {
            RaidContext? c;
            lock (Gate)
            {
                TryResolve();
                c = _current;
                _current = null;
                _pending = null;
            }
            if (c == null) return;
            ClientLink.SendReport(new RaidReport(
                c.S.MapId, c.S.Seed,
                new Dictionary<string, int>(c.Spawned), new Dictionary<string, int>(c.Dropped),
                c.Sanitized, new List<int[]>(c.AlivePerMinute), new Dictionary<string, int>(c.Killed)));
        }
    }
}
