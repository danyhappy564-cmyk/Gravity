using System;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using HarmonyLib;
using JsonType;
using SPT.Reflection.Patching;

namespace Gravity.Client.Patches
{
    /// <summary>
    /// Fika 호스트 레이드. Fika는 TarkovApplication.LocalGameCreate를 건너뛰고 CoopGame을 만들므로 LocalGame.Create와
    /// LocalGame.vmethod_1이 돌지 않는다. 호스트의 봇 시스템은 HostGameController가 만든다(생성자: 스폰 시나리오,
    /// InitializeBotsSystem: SetSettings·Init, StartBotsSystem: 시나리오 Run). Fika 2.4.4에서 확인.
    /// Fika 어셈블리는 참조하지 않고 이름으로 찾는다(Fika가 없으면 Plugin이 이 패치를 걸지 않음).
    /// </summary>
    internal static class FikaHost
    {
        public const string Guid = "com.fika.core";
        private const string ControllerTypeName = "Fika.Core.Main.GameMode.HostGameController";

        private static Type? _controllerType;

        /// <summary>처음 찾은 형식을 기억한다(패치를 걸 때 한 번 찾고, 레이드마다 다시 훑지 않음).</summary>
        public static Type ControllerType => _controllerType ??=
            AccessTools.TypeByName(ControllerTypeName) ?? throw new TypeLoadException(ControllerTypeName + " 없음");
    }

    /// <summary>
    /// HostGameController 생성자 뒤: 바닐라 RaidCreatePatch와 같이 이 레이드의 서버 설정 받기를 시작한다.
    /// 호스트에서만 만들어지므로(참가 클라는 ClientGameController) 봇을 내는 쪽에서만 받는다.
    /// </summary>
    internal sealed class FikaHostCreatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            var type = FikaHost.ControllerType;
            foreach (var ctor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
            {
                foreach (var p in ctor.GetParameters())
                    if (p.Name == "location" && p.ParameterType == typeof(LocationSettings.Location)) return ctor;
            }
            throw new MissingMethodException(type.FullName, ".ctor(..., LocationSettings.Location location, ...)");
        }

        [PatchPostfix]
        private static void Postfix(LocationSettings.Location location) => RaidCreatePatch.Begin(location, "FikaHostCreate");
    }

    /// <summary>HostGameController.StartBotsSystem 뒤: 바닐라 StartupPmcPatch(LocalGame.vmethod_1)와 같은 시작 분대 대기.</summary>
    internal sealed class FikaStartupPmcPatch : ModulePatch
    {
        private static FieldInfo? _botsController;

        protected override MethodBase GetTargetMethod()
        {
            var type = FikaHost.ControllerType;
            _botsController = type.GetField("_botsController", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(type.FullName, "_botsController");
            return type.GetMethod("StartBotsSystem", BindingFlags.Instance | BindingFlags.Public, null,
                       new[] { typeof(LocationSettings.Location) }, null)
                   ?? throw new MissingMethodException(type.FullName, "Task StartBotsSystem(LocationSettings.Location)");
        }

        [PatchPostfix]
        private static void Postfix(object __instance, ref Task __result)
        {
            if (__result == null || !Plugin.SpawnActive) return;
            // 바닐라의 controllerSettings.IsEnabled: InitializeBotsSystem이 BotsController.Init(botEnable)에 넘겨 _canSpawn이 된다.
            if (_botsController?.GetValue(__instance) is not BotsController bc || !bc._canSpawn) return;
            __result = StartupPmcPatch.WaitForStarts(__result, __instance, FikaHost.ControllerType);
        }
    }
}
