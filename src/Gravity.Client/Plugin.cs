using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Gravity.Client.Patches;
using Gravity.Shared;

namespace Gravity.Client
{
    /// <summary>
    /// Gravity 클라 플러그인. 서버 Gravity와 판을 맞추고(Hello), 레이드마다 서버 설정을 받아 맵 상한을 걸고,
    /// 봇 프로필의 깨진 외형·장비를 정리한다. ABPS가 깔려 있으면 스폰·정리 패치를 걸지 않는다.
    /// </summary>
    [BepInPlugin(Guid, "Gravity", GravityVersion.Current)]
    [BepInDependency(AbpsGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rf.gravity.client";
        public const string AbpsGuid = "com.acidphantasm.botplacementsystem";

        /// <summary>ABPS 플러그인이 같이 깔려 있으면 true(Awake에서 정함).</summary>
        public static bool AbpsPresent { get; private set; }

        /// <summary>마지막 Hello 응답. 실패했거나 아직 안 받았으면 null.</summary>
        public static HelloResponse? LastHello { get; internal set; }

        /// <summary>클라 스폰 기능을 켜도 되는지: Hello 성공, 판 호환, 서버가 잠그지 않음, ABPS 없음.</summary>
        public static bool SpawnActive
        {
            get
            {
                var h = LastHello;
                return h != null && h.Compatible && !h.SpawnLocked && !AbpsPresent;
            }
        }

        private void Awake()
        {
            Log.Init(Logger);
            AbpsPresent = Chainloader.PluginInfos.ContainsKey(AbpsGuid);

            // 메뉴 진입 때 Hello, 레이드 시작·끝, 맵 상한. ABPS 유무와 무관하게 연결은 한다(서버에 ABPS를 알리려고).
            Enable(new MenuHelloPatch());
            Enable(new RaidCreatePatch());
            Enable(new GameStartedPatch());
            Enable(new GameWorldDisposePatch());

            if (AbpsPresent)
            {
                Log.Info("ABPS 감지: 클라 스폰 패치와 프로필 정리를 걸지 않음");
            }
            else
            {
                Enable(new CapPatch());
                Enable(new ProfileSanitizerPatch());
                // PMC 지점 고르기(방식 A): 구역 0개 맵 보충·구역 가중 순서·지점 고르기·동시 그룹 제한·대기열 만료
                Enable(new PmcZonesPatch());
                Enable(new PmcZoneOrderPatch());
                Enable(new PmcZoneOrderPerfectPosPatch());
                Enable(new PmcPointsPatch());
                Enable(new PmcTrySpawnPatch());
                Enable(new PmcCapDelayPatch());
                // 게임 자체 상한 검사(BotBossSpawn.Spawn 안 CheckOnMax)도 Gravity 분대는 상한 제외 팩션을 빼고 본다(1.0, Codex 검토 1번)
                Enable(new BossSpawnScopePatch());
                Enable(new CheckOnMaxPatch());
                // 분대 대원 생성이 끝날 때 남은 예약 풀기, Gravity 스캐브 요청의 생성 자료 표시(게임 상한 검사에 팩션 제외, 재검토 1·3번)
                Enable(new BossSupportsPatch());
                Enable(new SpawnBossMainPatch());
                Enable(new ActivateBotPatch());
                Enable(new ScavWaveScopePatch());
                Enable(new CreationDataMarkPatch());
                Enable(new SpawnInZoneScopePatch());
                // 1분마다 살아 있는 봇 수 기록(보고용), 서버가 스캐브를 관리하면 바닐라 자동 스폰 대신 ScavDirector(F6·F7)
                Enable(new NonWavesPatch());
            }

            Log.Info($"클라 플러그인 {GravityVersion.Current} 준비");
        }

        private static void Enable(SPT.Reflection.Patching.ModulePatch patch)
        {
            try
            {
                patch.Enable();
            }
            catch (Exception ex)
            {
                Log.Warn($"패치 {patch.GetType().Name}를 걸지 못함: {ex.Message}");
            }
        }
    }

    /// <summary>로그 머리 [Gravity]. 같은 키의 예외는 한 번만 남긴다.</summary>
    internal static class Log
    {
        private static ManualLogSource? _src;
        private static readonly HashSet<string> Once = new HashSet<string>();

        public static void Init(ManualLogSource src) => _src = src;

        /// <summary>서버 diag.log(off·summary·verbose, 0.6). off면 알림은 안 남기고 경고·오류만.</summary>
        public static string Level = "summary";

        public static void Info(string msg) { if (Level != "off") _src?.LogInfo("[Gravity] " + msg); }

        public static void Verbose(string msg) { if (Level == "verbose") _src?.LogInfo("[Gravity] " + msg); }

        public static void Warn(string msg) => _src?.LogWarning("[Gravity] " + msg);

        /// <summary>key가 처음일 때만 남긴다. 처음이면 true.</summary>
        public static bool InfoOnce(string key, string msg)
        {
            if (!FirstTime(key)) return false;
            Info(msg);
            return true;
        }

        public static void WarnOnce(string key, string msg)
        {
            if (FirstTime(key)) Warn(msg);
        }

        public static void ErrorOnce(string where, Exception ex)
        {
            if (FirstTime("ex:" + where + ":" + ex.GetType().FullName + ":" + ex.Message))
                _src?.LogError($"[Gravity] {where} 예외(바닐라로 진행): {ex}");
        }

        private static bool FirstTime(string key)
        {
            lock (Once) return Once.Add(key);
        }
    }
}
