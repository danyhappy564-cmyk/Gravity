using System;
using System.Diagnostics;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Bots;
using EFT.Game.Spawning;
using Gravity.Shared;
using SPT.Reflection.Patching;

namespace Gravity.Client.Patches
{
    /// <summary>시작 분대의 몸체 준비를 로딩 단계에서 기다립니다. 상한·지점 규칙을 완화하거나 봇을 강제 생성하지 않습니다.</summary>
    internal sealed class StartupPmcPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(LocalGame).GetMethod("vmethod_1",
            BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(BotControllerSettings), typeof(ISpawnSystem) }, null)!;

        [PatchPostfix]
        private static void Postfix(LocalGame __instance, BotControllerSettings controllerSettings, ref Task __result)
        {
            if (__result == null || !controllerSettings.IsEnabled || !Plugin.SpawnActive) return;
            __result = WaitForStarts(__result, __instance);
        }

        private static async Task WaitForStarts(Task native, LocalGame game)
        {
            await native; // 원래 초기화의 실패·취소는 그대로 전달합니다.
            var c = RaidContext.Current;
            if (c == null || c.Started) return;
            var timer = Stopwatch.StartNew();
            int polls = 0;
            try
            {
                // 로딩을 더 기다리는 동안 후속 웨이브 시계가 흘러 먼저 생성되지 않도록 보존합니다.
                c.ResumeSpawnTimers = HoldLaterTimers(game, c);
                if (c.Startup.Expected == 0) return;
                int scavs = c.S.Scav.StartDelaySec <= 0 ? c.S.Scav.InitialCount : 0;
                if (c.S.Cap > 0 && c.Startup.Expected + scavs > c.S.Cap) return;
                if (c.S.GroupCap.On && c.S.GroupCap.Scope == "alive") return;
                if (c.S.Caps?.PmcGroups > 0 && (c.S.StartGroups?.Count ?? 0) > c.S.Caps.PmcGroups) return;
                while (c.Startup.ShouldWait(timer.Elapsed.TotalSeconds, polls,
                    ReferenceEquals(RaidContext.Current, c), c.Started, Full(c)))
                {
                    polls++;
                    await Task.Delay(StartupPreparation.PollMilliseconds);
                }
                if (!ReferenceEquals(RaidContext.Current, c)) return;
                if (c.Startup.Prepared >= c.Startup.Expected)
                    Log.Info($"{c.S.MapId} 시작 PMC 준비 {c.Startup.Prepared}/{c.Startup.Expected}명: 추가 로딩 {timer.Elapsed.TotalSeconds:0.0}초");
                else Log.Warn($"{c.S.MapId} 시작 PMC 준비 대기 종료 {c.Startup.Prepared}/{c.Startup.Expected}명({timer.Elapsed.TotalSeconds:0.0}초): 남은 생성은 기존 대기열로 진행");
            }
            catch (Exception ex)
            {
                c.ResumeSpawnTimers?.Invoke();
                c.ResumeSpawnTimers = null;
                Log.ErrorOnce("StartupPmc", ex); // 추가 대기 실패가 레이드 진입을 막지 않게 합니다.
            }
        }

        private static Action HoldLaterTimers(LocalGame game, RaidContext c)
        {
            var held = new List<(TimerManager.ITimer timer, float remaining)>();
            void Hold(List<TimerManager.ITimer>? list, bool keepInitial)
            {
                if (list == null) return;
                for (int i = 0; i < list.Count && i < 1024; i++)
                {
                    var timer = list[i];
                    if (timer == null || !timer.IsActive || keepInitial && timer.Duration <= 5f) continue;
                    held.Add((timer, Math.Max(0, timer.TimeLeft)));
                    // Restart는 네이티브 타이머 정렬 목록에도 새 종료 시각을 반영합니다.
                    timer.Restart(float.MaxValue);
                }
            }
            void Resume()
            {
                bool same = ReferenceEquals(RaidContext.Current, c);
                for (int i = 0; i < held.Count; i++)
                {
                    var entry = held[i];
                    if (!entry.timer.IsActive) continue;
                    if (same) entry.timer.Restart(Time.time + entry.remaining);
                    else entry.timer.Stop();
                }
                held.Clear();
            }
            try
            {
                const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
                var scavs = typeof(LocalGame).GetField("_wavesSpawnScenario", fields)?.GetValue(game) as WavesSpawnScenario;
                var list = typeof(WavesSpawnScenario).GetField("Timers", fields)?.GetValue(scavs) as List<TimerManager.ITimer>;
                Hold(list, false);
                var bosses = typeof(LocalGame).GetField("_bossSpawnScenario", fields)?.GetValue(game) as BossSpawnScenario;
                Hold(bosses?.Timers, true); // 시작 PMC 1~5초 타이머는 준비를 진행하게 둡니다.
                return Resume;
            }
            catch { Resume(); throw; }
        }

        private static bool Full(RaidContext c)
        {
            if (c.S.Cap <= 0) return false;
            var world = Singleton<GameWorld>.Instance;
            if (world == null) return false;
            int occupied = 0;
            var alive = world.AllAlivePlayersList;
            for (int i = 0; i < alive.Count && i < 256; i++)
            {
                var p = alive[i];
                if (p == null || !p.IsAI) continue;
                var role = p.Profile?.Info?.Settings?.Role;
                if (role == null || !c.IsCapExcluded(role.Value)) occupied++;
            }
            return occupied >= c.S.Cap;
        }
    }
}
