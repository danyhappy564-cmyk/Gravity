using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.UI;
using Gravity.Shared;
using JsonType;
using SPT.Reflection.Patching;

namespace Gravity.Client.Patches
{
    /// <summary>메뉴 화면이 열릴 때마다 서버에 Hello(기다리지 않음). 결과는 Plugin.LastHello.</summary>
    internal sealed class MenuHelloPatch : ModulePatch
    {
        private static Task? _inFlight;
        private static string _lastState = "";

        protected override MethodBase GetTargetMethod() =>
            typeof(MenuScreen).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .First(m => m.Name == "Show" && m.GetParameters().Length == 3);

        [PatchPostfix]
        private static void Postfix()
        {
            try
            {
                if (_inFlight != null && !_inFlight.IsCompleted) return;
                _inFlight = Run();
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("MenuHello", ex);
            }
        }

        private static async Task Run()
        {
            var h = await ClientLink.HelloAsync();
            Plugin.LastHello = h;
            var state = h == null ? "fail"
                : $"{h.ServerVersion}|{h.Compatible}|{h.SpawnLocked}|{Plugin.AbpsPresent}";
            if (state == _lastState) return; // 같은 결과는 한 번만 알린다
            _lastState = state;
            if (h == null) return; // 경고는 ClientLink가 남김
            if (!h.Compatible)
                Log.Warn($"서버 판({h.ServerVersion})과 클라 판({GravityVersion.Current})이 맞지 않음: 클라 스폰 끔");
            else if (h.SpawnLocked)
                Log.Info($"서버 연결 성공({h.ServerVersion}), 서버가 스폰 잠금(ABPS 등): 클라 스폰 끔");
            else
                Log.Info($"서버 연결 성공({h.ServerVersion})");
        }
    }

    /// <summary>LocalGame.Create 뒤: 이 레이드의 서버 설정 받기를 시작한다(BotsController.SetSettings보다 먼저 돈다).</summary>
    internal sealed class RaidCreatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(LocalGame).GetMethod(nameof(LocalGame.Create), BindingFlags.Static | BindingFlags.Public)!;

        [PatchPostfix]
        private static void Postfix(LocationSettings.Location location)
        {
            try
            {
                if (!Plugin.SpawnActive || location == null) return;
                RaidContext.BeginFetch(location.Id);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("RaidCreate", ex);
            }
        }
    }

    /// <summary>GameWorld.OnGameStarted 뒤: 늦게 온 설정을 붙이고, 그때도 없으면 그 레이드는 바닐라.</summary>
    internal sealed class GameStartedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(GameWorld).GetMethod(nameof(GameWorld.OnGameStarted), BindingFlags.Instance | BindingFlags.Public)!;

        [PatchPostfix]
        private static void Postfix()
        {
            try
            {
                RaidContext.OnGameStarted();
                var c = RaidContext.Current;
                if (c != null && !c.CapApplied && !Plugin.AbpsPresent && Singleton<IBotGame>.Instantiated)
                    CapPatch.Apply(Singleton<IBotGame>.Instance.BotsController, c);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("GameStarted", ex);
            }
        }
    }

    /// <summary>GameWorld.Dispose 앞: 레이드 보고를 보내고 RaidContext를 지운다.</summary>
    internal sealed class GameWorldDisposePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(GameWorld).GetMethod(nameof(GameWorld.Dispose), BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)!;

        [PatchPrefix]
        private static void Prefix()
        {
            try
            {
                RaidContext.End();
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("GameWorldDispose", ex);
            }
        }
    }
}
