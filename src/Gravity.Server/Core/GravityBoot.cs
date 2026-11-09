using System.Reflection;
using SPTarkov.Server.Core.Helpers.Server;

namespace Gravity.Core;

/// <summary>
/// 서버 시작 때 설정 파일 읽기·다른 모드 감지를 한 번만 한다. 이벤트 로더(SPT 이벤트 적용 직전)와 본 로더(PostLoad)가 같이 쓴다.
/// </summary>
public static class GravityBoot
{
    private static readonly object Gate = new();
    public static GravityPaths? Paths { get; private set; }
    public static GravityConfigStore? Store { get; private set; }
    public static LoadResult? Load { get; private set; }
    public static DetectResult? Detect { get; private set; }
    public static string? ModsDir { get; private set; }

    public static void Ensure(ModHelper modHelper)
    {
        lock (Gate)
        {
            if (Store != null) return;
            var paths = new GravityPaths(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()));
            var store = new GravityConfigStore(paths.ConfigFile, paths.BackupDir);
            Load = store.Load();
            var modsDir = Path.GetDirectoryName(paths.ModDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!;
            var folders = Directory.Exists(modsDir) ? Directory.GetDirectories(modsDir).Select(Path.GetFileName).OfType<string>() : [];
            var asms = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).OfType<string>();
            Detect = ModDetector.Detect(asms, folders, store.Current.CompatOrDefault);
            ModsDir = modsDir;
            Paths = paths;
            Store = store;
        }
    }
}
