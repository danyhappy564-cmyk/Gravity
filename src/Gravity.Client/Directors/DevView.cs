using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using Gravity.Client.Patches;
using Gravity.Shared;
using UnityEngine;

namespace Gravity.Client.Directors
{
    /// <summary>
    /// 1.1.0 클라이언트·개발자 화면.
    /// - 봇 미리 만들어 두기(S.Preload): 레이드 시작 뒤 한 번 게임 예비 프로필 목표(BotSpawner.AddToTargetBackup)에 넣는다.
    ///   게임이 30초마다 목표만큼 프로필을 미리 받아 두므로 레이드 중 분대가 나올 때 서버에 묻는 끊김이 준다. 이미 목표가 있는 역할은 게임이 그대로 둔다.
    /// - 지금 넣어 보기: 레이드 중 5초마다 서버(/gravity-client/poll)에 묻고, 받은 명령을 게임 스레드에서 바로 넣는다(맵 상한 무시, Gravity 장부 밖).
    /// - 게임 화면 표시(S.Dev): 봇 수·다음 분대 시각(Overlay), 가까운 스폰 지점(Markers), 구역 이름·대략의 둘레(Zones). OnGUI 글자만 쓴다.
    /// </summary>
    internal static class DevView
    {
        private const float PollSec = 5f;
        private static RaidContext? _ctx;
        private static bool _preloaded;
        private static float _nextPoll;
        private static Task<DevPollResponse?>? _poll;
        private static readonly Queue<DevCommand> Todo = new Queue<DevCommand>();

        // 표시용 자료(레이드에서 한 번 모음)
        private static readonly List<(Vector3 Pos, bool Pmc)> Points = new List<(Vector3, bool)>();
        private static readonly List<(string Name, Vector3 Center, float Radius, int Count)> ZoneInfo = new List<(string, Vector3, float, int)>();
        private static bool _built;
        private static GUIStyle? _small, _box;

        /// <summary>Plugin.Update에서 매 프레임.</summary>
        internal static void Update()
        {
            var c = RaidContext.Current;
            if (!ReferenceEquals(c, _ctx))
            {
                _ctx = c; _preloaded = false; _built = false; _poll = null; Todo.Clear(); Points.Clear(); ZoneInfo.Clear();
                _nextPoll = 0;
            }
            if (c == null || !c.Started) return;
            try
            {
                if (!_preloaded) Preload(c);
                Poll(c);
                if (Todo.Count > 0) Run(c, Todo.Dequeue());
            }
            catch (Exception ex) { Log.ErrorOnce("DevView.Update", ex); }
        }

        private static BotsController? Controller() =>
            Singleton<IBotGame>.Instantiated ? Singleton<IBotGame>.Instance.BotsController : null;

        private static void Preload(RaidContext c)
        {
            var spawner = Controller()?.BotSpawner;
            if (spawner == null) return;
            _preloaded = true;
            var list = c.S.Preload;
            if (list == null || list.Count == 0) return;
            int total = 0;
            foreach (var p in list)
            {
                if (!Enum.TryParse<WildSpawnType>(p.Role, true, out var role) || !Enum.TryParse<BotDifficulty>(p.Difficulty, true, out var d) || p.Count <= 0) continue;
                spawner.AddToTargetBackup(d, role, p.Count);
                total += p.Count;
            }
            Log.Info($"{c.S.MapId} 봇 미리 만들어 두기: 예비 프로필 목표 {total}명({list.Count}종)");
        }

        private static void Poll(RaidContext c)
        {
            if (_poll != null)
            {
                if (!_poll.IsCompleted) return;
                var r = _poll.Status == TaskStatus.RanToCompletion ? _poll.Result : null;
                _poll = null;
                if (r?.Commands != null) foreach (var cmd in r.Commands) Todo.Enqueue(cmd);
            }
            if (Time.time < _nextPoll) return;
            _nextPoll = Time.time + PollSec;
            _poll = ClientLink.PollAsync(c.S.MapId);
        }

        private static void Run(RaidContext c, DevCommand cmd)
        {
            var bc = Controller();
            var spawner = bc?.BotSpawner;
            if (bc == null || spawner == null) return;
            var gw = Singleton<GameWorld>.Instance;
            var player = gw?.MainPlayer;
            if (cmd.Kind == "scav")
            {
                var zones = new List<BotZone>();
                foreach (var z in spawner.SpawnZones(false)) if (z != null && z.SpawnPoints != null && z.SpawnPoints.Length > 0) zones.Add(z);
                if (zones.Count == 0) { Log.Warn($"{c.S.MapId} 지금 넣어 보기: 스캐브 구역 없음"); return; }
                var zone = zones[c.Rng.Next(zones.Count)];
                bc.ActivateBotsByWave(WaveFactory.Scav(zone.NameZone, BotDifficulty.normal, false));
                Log.Info($"{c.S.MapId} 지금 넣어 보기: 스캐브 1명({zone.NameZone})");
                return;
            }
            Vector3? at = null;
            if (cmd.Kind == "near" && player != null)
            {
                // 플레이어 둘레 근처 보충 거리(설정 값)의 스캐브 지점 하나
                var n = c.S.Near;
                double min = n?.MinDist ?? 80, max = n?.MaxDist ?? 200;
                var best = new List<Vector3>();
                foreach (var z in spawner.SpawnZones(false))
                {
                    if (z?.SpawnPoints == null) continue;
                    foreach (var sp in z.SpawnPoints)
                    {
                        if (sp == null) continue;
                        var d = Vector3.Distance(sp.Position, player.Position);
                        if (d >= min && d <= max) best.Add(sp.Position);
                    }
                }
                if (best.Count == 0) { Log.Warn($"{c.S.MapId} 지금 넣어 보기: {min:0}~{max:0}m 안에 지점 없음"); return; }
                at = best[c.Rng.Next(best.Count)];
            }
            var wave = WaveFactory.Custom(cmd.Role, cmd.Escort, Math.Max(0, cmd.Escorts), cmd.Difficulty, at);
            if (wave == null) { Log.Warn($"{c.S.MapId} 지금 넣어 보기: 역할 '{cmd.Role}'을 모름(그 모드가 없음)"); return; }
            wave.IgnoreMaxBots = true;
            bc.ActivateBotsByWave(wave);
            Log.Info($"{c.S.MapId} 지금 넣어 보기: {cmd.Kind} {cmd.Role} {1 + cmd.Escorts}명");
        }

        /// <summary>Plugin.OnGUI에서.</summary>
        internal static void OnGUI()
        {
            var c = RaidContext.Current;
            var dev = c?.S.Dev;
            if (c == null || dev == null || !c.Started || Event.current.type != EventType.Repaint) return;
            try
            {
                var cam = Camera.main;
                var player = Singleton<GameWorld>.Instance?.MainPlayer;
                if (cam == null || player == null) return;
                _small ??= new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true };
                _box ??= new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.UpperLeft, richText = true };
                if (dev.Markers || dev.Zones) Build(c);
                if (dev.Overlay) Overlay(c);
                var pp = player.Position;
                if (dev.Markers)
                {
                    int shown = 0;
                    foreach (var (pos, pmc) in Points)
                    {
                        if (shown >= 150 || (pos - pp).sqrMagnitude > 150f * 150f) continue;
                        if (Label(cam, pos + Vector3.up * 0.5f, pmc ? "<color=#6ea8ff>◆P</color>" : "<color=#f0c070>●S</color>")) shown++;
                    }
                }
                if (dev.Zones)
                {
                    foreach (var (name, center, radius, count) in ZoneInfo)
                    {
                        if ((center - pp).sqrMagnitude > 500f * 500f) continue;
                        Label(cam, center + Vector3.up * 2f, $"<b><color=#a78bfa>{name}</color></b> ({count})");
                        for (int i = 0; i < 16; i++)
                        {
                            float a = i * Mathf.PI / 8f;
                            Label(cam, center + new Vector3(Mathf.Cos(a) * radius, 0.5f, Mathf.Sin(a) * radius), "<color=#a78bfa>·</color>");
                        }
                    }
                }
            }
            catch (Exception ex) { Log.ErrorOnce("DevView.OnGUI", ex); }
        }

        private static bool Label(Camera cam, Vector3 world, string text)
        {
            var s = cam.WorldToScreenPoint(world);
            if (s.z <= 0) return false;
            GUI.Label(new Rect(s.x - 20, Screen.height - s.y - 8, 220, 20), text, _small);
            return true;
        }

        private static void Overlay(RaidContext c)
        {
            int pmc = 0, scav = 0, boss = 0;
            foreach (var a in PmcZones.Actors())
            {
                if (a.Kind == ActorKind.Pmc) pmc++;
                else if (a.Kind == ActorKind.Scav) scav++;
                else if (a.Kind == ActorKind.Boss) boss++;
            }
            int now = (int)c.Elapsed;
            int next = -1;
            if (c.S.PlannedTimes != null)
                foreach (var t in c.S.PlannedTimes) if (t > now && (next < 0 || t < next)) next = t;
            var bc = Controller();
            string cap = bc != null ? bc._maxCount.ToString() : "?";
            string text = $"<b>Gravity</b> {c.S.MapId}  {now / 60}:{now % 60:00}\n봇 PMC {pmc} · 스캐브 {scav} · 보스 {boss} / 상한 {cap}\n"
                + (next >= 0 ? $"다음 계획 PMC 분대 {next - now}초 뒤" : "남은 계획 PMC 분대 없음");
            GUI.Box(new Rect(10, 10, 300, 62), text, _box);
        }

        private static void Build(RaidContext c)
        {
            if (_built) return;
            var spawner = Controller()?.BotSpawner;
            if (spawner == null) return;
            _built = true;
            PmcZones.EnsurePoints(c);
            foreach (var kv in c.ZonePoints)
                foreach (var cand in kv.Value) Points.Add((new Vector3(cand.Pos.X, cand.Pos.Y, cand.Pos.Z), true));
            foreach (var z in spawner.SpawnZones(false))
            {
                if (z?.SpawnPoints == null || z.SpawnPoints.Length == 0) continue;
                var sum = Vector3.zero;
                int n = 0;
                foreach (var sp in z.SpawnPoints) { if (sp == null) continue; Points.Add((sp.Position, false)); sum += sp.Position; n++; }
                if (n == 0) continue;
                var center = sum / n;
                float r = 0;
                foreach (var sp in z.SpawnPoints) if (sp != null) r = Mathf.Max(r, Vector3.Distance(new Vector3(sp.Position.x, center.y, sp.Position.z), center));
                ZoneInfo.Add((z.NameZone, center, Mathf.Max(5f, r), n));
            }
            Log.Info($"{c.S.MapId} 게임 화면 표시: 지점 {Points.Count}개, 구역 {ZoneInfo.Count}개");
        }
    }
}
