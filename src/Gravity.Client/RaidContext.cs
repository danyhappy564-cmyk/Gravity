using System.Collections.Generic;
using System.Threading.Tasks;
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

        private RaidContext(ClientRaidSettings s, float startTime)
        {
            S = s;
            StartTime = startTime;
        }

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
                if (_current != null) _current.StartTime = Time.time;
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
                new Dictionary<string, int>(), new Dictionary<string, int>(),
                c.Sanitized, new List<int[]>()));
        }
    }
}
