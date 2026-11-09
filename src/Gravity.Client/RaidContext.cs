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

        /// <summary>살아서 맵을 떠난 봇 수(0.6, 판정 56): BotDespawn도 BotDied를 거치므로 OnBotRemoved 때 살아 있으면 탈출로 센다.</summary>
        public readonly Dictionary<string, int> Extracted = new Dictionary<string, int> { ["pmcUSEC"] = 0, ["pmcBEAR"] = 0, ["assault"] = 0, ["marksman"] = 0, ["boss"] = 0, ["other"] = 0 };

        /// <summary>무한 방식 PMC 보충 목록(서버 RefillGroups 복사본).</summary>
        public readonly List<RefillGroup> Refill;

        /// <summary>탈출 보충(0.6) 대기열: 다음 PMC 차례에 Refill보다 먼저 쓴다(목록 끝에 붙이면 하드 스톱 전에 차례가 안 올 수 있음, Codex 지적 4).</summary>
        public readonly Queue<RefillGroup> ExtractQueue = new Queue<RefillGroup>();

        /// <summary>탈출 보충으로 더한 분대 수(서버 ExtractRefillCap까지).</summary>
        public int ExtractRefill;

        /// <summary>이 레이드의 BotSpawner.OnBotRemoved에 붙었는지(게임 스레드).</summary>
        internal EFT.BotSpawner? DeathHookedTo;

        /// <summary>버린 수(키 noPoint·expired·groupCap·cap). cap = 맵 상한 대기 중 하드 스톱에 닿아 버린 PMC 분대 +
        /// ScavDirector 차례가 상한에 막힌 구간 수(막힌 차례가 이어지는 동안 한 번) + 상한에 막힌 근처 보충 자리 수.
        /// noPoint에는 지점을 못 찾은 근처 보충 자리 수도 들어간다. 보고용.</summary>
        public readonly Dictionary<string, int> Dropped = new Dictionary<string, int> { ["noPoint"] = 0, ["expired"] = 0, ["groupCap"] = 0, ["cap"] = 0, ["scavCap"] = 0, ["nearCap"] = 0 };

        private HashSet<string>? _capExclude;
        private HashSet<string>? _disabledZones;

        /// <summary>이번 배치에서 끈 구역. 설정 목록은 레이드에서 한 번만 집합으로 바꾼다.</summary>
        internal bool IsZoneDisabled(string name)
        {
            if (S.DisabledZones == null || S.DisabledZones.Count == 0) return false;
            if (_disabledZones == null) _disabledZones = new HashSet<string>(S.DisabledZones, StringComparer.Ordinal);
            return _disabledZones.Contains(name);
        }

        private HashSet<string>? _factionRoles;
        private readonly Dictionary<EFT.WildSpawnType, string> _kindCache = new Dictionary<EFT.WildSpawnType, string>();

        /// <summary>상한 표 종류(CapKinds: pmc·scav·boss·special·faction·event·other). 팩션 모드 역할은 이름이나 번호로 맞춘다.</summary>
        internal string KindOf(EFT.WildSpawnType role)
        {
            if (_kindCache.TryGetValue(role, out var k)) return k;
            if (_factionRoles == null) _factionRoles = new HashSet<string>(S.Caps?.FactionRoles ?? S.CapExclude ?? (IReadOnlyList<string>)Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            k = _factionRoles.Contains(((int)role).ToString()) ? CapKinds.Faction : CapKinds.Of(role.ToString(), _factionRoles);
            _kindCache[role] = k;
            return k;
        }

        /// <summary>맵 상한에서 뺄 봇 역할인지. 상한 표(1.0)가 있으면 "따로 셈" 종류, 옛 서버면 CapExclude(관리 중이고 상한을 같이 쓰지 않는 팩션, 0.4).</summary>
        internal bool IsCapExcluded(EFT.WildSpawnType role)
        {
            if (S.Caps != null) return CapKinds.Excluded(S.Caps, KindOf(role));
            var l = S.CapExclude;
            if (l == null || l.Count == 0) return false;
            if (_capExclude == null) _capExclude = new HashSet<string>(l, StringComparer.OrdinalIgnoreCase);
            return _capExclude.Contains(role.ToString()) || _capExclude.Contains(((int)role).ToString());
        }

        /// <summary>살아 있는 봇 중 맵 상한에서 뺄 팩션 봇 수.</summary>
        internal readonly HashSet<string> ExtraSpecialProfiles = new HashSet<string>(StringComparer.Ordinal);
        internal bool IsCapExcluded(EFT.Player p) => !ExtraSpecialProfiles.Contains(p.Profile.Id)
            && p.Profile?.Info?.Settings != null && IsCapExcluded(p.Profile.Info.Settings.Role);

        internal int CountCapExcluded()
        {
            if (S.Caps == null && (S.CapExclude == null || S.CapExclude.Count == 0)) return 0;
            var gw = Comfort.Common.Singleton<EFT.GameWorld>.Instance;
            if (gw == null) return 0;
            int n = 0;
            var alive = gw.AllAlivePlayersList;
            for (int i = 0; i < alive.Count; i++)
            {
                var p = alive[i];
                var role = p != null && p.IsAI ? p.Profile?.Info?.Settings?.Role : null;
                if (role != null && IsCapExcluded(p!)) n++;
            }
            return n;
        }

        /// <summary>살아 있는 봇 중 이 종류 수(상한 표 최대 인원).</summary>
        internal int CountKind(string kind)
        {
            var gw = Comfort.Common.Singleton<EFT.GameWorld>.Instance;
            if (gw == null) return 0;
            int n = 0;
            var alive = gw.AllAlivePlayersList;
            for (int i = 0; i < alive.Count; i++)
            {
                var p = alive[i];
                var role = p != null && p.IsAI ? p.Profile?.Info?.Settings?.Role : null;
                if (role != null && KindOf(role.Value) == kind) n++;
            }
            return n;
        }

        /// <summary>살아 있는 PMC 봇 수와 무리(BotsGroup) 수(상한 표 PMC 예약 자리·동시 무리 최대).</summary>
        private readonly HashSet<object> _pmcGroups = new HashSet<object>();

        internal int CountPmc(out int groups)
        {
            groups = 0;
            var gw = Comfort.Common.Singleton<EFT.GameWorld>.Instance;
            if (gw == null) return 0;
            int n = 0;
            var seen = _pmcGroups;
            seen.Clear();
            var alive = gw.AllAlivePlayersList;
            for (int i = 0; i < alive.Count; i++)
            {
                var p = alive[i];
                var role = p != null && p.IsAI ? p.Profile?.Info?.Settings?.Role : null;
                if (role == null || KindOf(role.Value) != CapKinds.Pmc) continue;
                n++;
                object g = (object?)p!.AIData?.BotOwner?.BotsGroup ?? p;
                if (seen.Add(g)) groups++;
            }
            return n;
        }

        /// <summary>스캐브·근처 보충이 비워 둘 PMC 예약 자리(아직 PMC가 안 채운 몫). 상한 표가 없거나 0이면 0.</summary>
        internal int PmcReserveLeft()
        {
            int reserve = S.Caps?.PmcReserve ?? 0;
            if (reserve <= 0) return 0;
            SyncRequests();
            return CapKinds.ReserveLeft(reserve, CountPmc(out _) + Requests.Pending(CapKinds.Pmc) + (ScavState?.Priority.Size ?? 0));
        }

        /// <summary>PMC 동시 무리 최대에 닿았는지(상한 표, 0이면 제한 없음).</summary>
        internal bool PmcGroupsFull(object? except = null)
        {
            int max = S.Caps?.PmcGroups ?? 0;
            if (max <= 0) return false;
            CountPmc(out int groups);
            SyncRequests();
            return groups + Requests.PendingPmcGroups(except) >= max;
        }

        internal readonly SpawnRequestLedger Requests = new SpawnRequestLedger();

        /// <summary>생존 목록으로 넘어간 예약만 빼며 버퍼를 재사용합니다. 프레임 루프에서는 호출하지 않습니다.</summary>
        internal void SyncRequests()
        {
            Requests.BeginSnapshot();
            var gw = Comfort.Common.Singleton<EFT.GameWorld>.Instance;
            if (gw == null) return;
            var alive = gw.AllAlivePlayersList;
            for (int i = 0; i < alive.Count; i++)
            {
                var p = alive[i];
                if (p != null && p.IsAI) Requests.Observe(p.Profile.Id);
            }
        }

        /// <summary>맵 동시 상한 예약 장부(1.0, Codex 검토 1번): Gravity가 요청했지만 아직 게임 집계에 안 잡힌 인원.</summary>
        internal readonly CapLedger Ledger = new CapLedger();

        /// <summary>ScavDirector가 넣은 스캐브 웨이브(값 없음) → 그 웨이브로 만들어진 생성 자료(재검토 3번: 게임 상한 검사에 팩션 제외).</summary>
        internal readonly ConditionalWeakTable<EFT.SpawnWave, object> ScavWaves = new ConditionalWeakTable<EFT.SpawnWave, object>();
        internal readonly ConditionalWeakTable<global::BotCreationData, EFT.SpawnWave> ScavData = new ConditionalWeakTable<global::BotCreationData, EFT.SpawnWave>();

        /// <summary>Gravity가 낸 분대 웨이브(무한·탈출 보충 등록 + 서버 계획 분대 중 서명이 맞아 알아본 것). 값 없음.</summary>
        internal readonly ConditionalWeakTable<BossLocationSpawn, object> Owned = new ConditionalWeakTable<BossLocationSpawn, object>();

        /// <summary>
        /// 이 분대 웨이브가 Gravity 것인지: 근처 보충·클라 보충(등록)·서버 계획 분대(TriggerId 표식 gravity:…, 4차 재검토 2번).
        /// 다른 모드가 넣은 PMC는 false → 상한 보정·예약·그룹 제한·대기열 만료를 걸지 않는다. 옛 서버(WaveMark 없음)는 예전처럼 PMC 역할이면 true.
        /// </summary>
        internal bool Owns(BossLocationSpawn? w)
        {
            if (w == null) return false;
            if (NearWaves.TryGetValue(w, out _) || Owned.TryGetValue(w, out _)) return true;
            if (!w.BossType.IsPmcBot()) return false;
            if (S.WaveMark == null) return true;
            if (!Gravity.Shared.WaveMark.Is(w.TriggerId, w.TriggerName)) return false;
            Owned.Add(w, w);
            return true;
        }

        /// <summary>
        /// 분대 생성 추적(4차 재검토 1번). 분대장 생성 자료 → 분대, 분대장 봇 → 분대. 분대장이 만들어지지 못했거나(생성 Task가 콜백 없이 끝남)
        /// 활성화에 실패했거나(ActiveFail) 대원 생성 전에 맵에서 빠지면, 대원은 영영 나오지 않으므로 남은 대원 예약을 푼다.
        /// 대원 생성이 시작됐으면(SupportsStarted) 그 작업이 끝날 때 BossSupportsPatch가 푼다.
        /// </summary>
        internal sealed class SquadWatch
        {
            public readonly BossLocationSpawn Wave;
            public readonly SquadLifecycle Life = new SquadLifecycle();
            public bool LeaderSeen => Life.LeaderSeen;
            public bool SupportsStarted => Life.SupportsStarted;
            public global::BotCreationData? LeaderData;
            public bool EscortTask;
            public bool Submitted;
            public int Requested;
            public SquadWatch(BossLocationSpawn wave) => Wave = wave;
        }

        internal readonly ConditionalWeakTable<global::BotSpawnParams, SquadWatch> ParamsOf = new ConditionalWeakTable<global::BotSpawnParams, SquadWatch>();
        internal readonly ConditionalWeakTable<global::BotCreationData, SquadWatch> DataOf = new ConditionalWeakTable<global::BotCreationData, SquadWatch>();
        internal readonly ConditionalWeakTable<global::BotCreationData, SquadWatch> SquadOf = new ConditionalWeakTable<global::BotCreationData, SquadWatch>();
        internal readonly ConditionalWeakTable<BossLocationSpawn, SquadWatch> WatchOf = new ConditionalWeakTable<BossLocationSpawn, SquadWatch>();
        internal readonly ConditionalWeakTable<EFT.BotOwner, SquadWatch> LeaderOf = new ConditionalWeakTable<EFT.BotOwner, SquadWatch>();

        internal SquadWatch WatchWave(BossLocationSpawn wave, global::BotSpawnParams? parameters)
        {
            if (!WatchOf.TryGetValue(wave, out var w)) WatchOf.Add(wave, w = new SquadWatch(wave));
            if (parameters != null && !ParamsOf.TryGetValue(parameters, out _)) ParamsOf.Add(parameters, w);
            return w;
        }

        internal bool Tracks(BossLocationSpawn wave) => WatchOf.TryGetValue(wave, out _) || Owns(wave);

        internal void CancelWave(BossLocationSpawn wave)
        {
            Ledger.Release(wave);
            Requests.Abort(wave);
            if (WatchOf.TryGetValue(wave, out var w)) w.Life.Terminate();
        }

        internal void FinishSubmission(SquadWatch w)
        {
            if (w.Submitted) return;
            w.Submitted = true;
            Ledger.Release(w.Wave);
            // 요청조차 하지 못한 대원만 뺍니다. 로딩·대기·활성화 중인 자료의 예약은 그대로 유지합니다.
            Requests.Fail(w.Wave, Math.Max(0, 1 + w.Wave.EscortCount - w.Requested));
        }

        /// <summary>분대장 생성 자료에 분대 추적을 붙인다(같은 자료로 다시 와도 하나만).</summary>
        internal void WatchSquad(global::BotCreationData data, BossLocationSpawn wave)
        {
            if (data == null || wave == null || SquadOf.TryGetValue(data, out _)) return;
            if (!WatchOf.TryGetValue(wave, out var w)) WatchOf.Add(wave, w = new SquadWatch(wave));
            w.LeaderData = data;
            SquadOf.Add(data, w);
            if (!DataOf.TryGetValue(data, out _)) DataOf.Add(data, w);
        }

        /// <summary>대원 생성 전에 분대가 끝났다: 남은 대원 예약을 푼다(대원 생성이 이미 시작됐으면 그 끝에서 푼다).</summary>
        internal void SquadFailed(SquadWatch w, string why)
        {
            if (w == null || !w.Life.Terminate()) return;
            Requests.Abort(w.Wave);
            if (Ledger.Release(w.Wave)) Log.Verbose($"{S.MapId} 분대장 {why}: 대원 예약을 풂");
        }

        /// <summary>"레이드 전체" 그룹 제한 장부(1.0, Codex 검토 2번): 서버 계획 분대(S.PlannedGroups)로 시작해 클라 보충 분대를 더한다.</summary>
        internal readonly RaidGroupLedger Groups;

        /// <summary>게임이 세는 점유: 생존·생성 중·로딩·대기열·프로필 받는 중. 두 집계가 겹칠 수 있어 큰 쪽을 쓴다.</summary>
        internal static int Occupied(EFT.BotSpawner sp) =>
            Math.Max(sp.AllBotsWithDelayed, sp.AllBotsWithLoaded + (sp.SpawnDelaysService?.WaitCount ?? 0));

        /// <summary>맵 상한 빈자리 = 상한 − (점유 − 상한 제외 팩션) − 예약. 상한이 없으면 int.MaxValue.</summary>
        internal int Room(EFT.BotsController bc) =>
            CapLedger.Room(bc._maxCount, Occupied(bc.BotSpawner), CountCapExcluded(), Ledger.Held(Time.time));

        /// <summary>자리가 있으면 size명을 예약한다(key = 생성 요청: 분대 웨이브 또는 스캐브 요청 표식).</summary>
        internal bool TryReserve(EFT.BotsController bc, object key, int size) =>
            Ledger.TryReserve(key, size, bc._maxCount, Occupied(bc.BotSpawner), CountCapExcluded(), Time.time);

        internal int ScavRoom(EFT.BotsController bc) => SpawnCapacity.RoomForScavs(bc._maxCount,
            Occupied(bc.BotSpawner), CountCapExcluded(), Ledger.Held(Time.time), PmcReserveLeft());

        internal bool TryReserveScav(EFT.BotsController bc, object key, int size) =>
            ScavRoom(bc) >= size && TryReserve(bc, key, size);

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
            Refill = new List<RefillGroup>(s.RefillGroups ?? (IReadOnlyList<RefillGroup>)Array.Empty<RefillGroup>());
            Groups = new RaidGroupLedger(s.GroupCap, s.PlannedGroups);
            Log.Level = s.LogLevel ?? "summary";
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
                // 현재 서버가 이 판 관리를 허용했으므로, 옛 메뉴 Hello의 QB 잠금도 갱신한다.
                if (Plugin.LastHello is { SpawnLocked: true } hello) Plugin.LastHello = hello with { SpawnLocked = false };
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
                c.Sanitized, new List<int[]>(c.AlivePerMinute), new Dictionary<string, int>(c.Killed),
                new Dictionary<string, int>(c.Extracted), c.ExtractRefill));
        }
    }
}
