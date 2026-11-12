using System.Globalization;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Web;

namespace Gravity;

/// <summary>
/// IModBlazorMetadata: SPT 런처·SIC의 "모드 페이지" 목록에 Gravity 웹(/gravity/)을 올린다(사용자 11-10: 목록에 Gravity가 없음).
/// 이 인터페이스가 있으면 SPT가 wwwroot를 정적 파일로도 연결하는데, 우리 웹 주소(/gravity)와 겹치지 않게 WWWRootUrl을 따로 둔다.
/// </summary>
public record ModMetadata : IModMetadata, IModBlazorMetadata
{
    public string ModGuid { get; init; } = "com.rf.gravity";
    public string Name { get; init; } = "Gravity";
    public string Author { get; init; } = "R_F";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new(GravityInfo.Version);
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/danyhappy564-cmyk/Gravity";
    public string License { get; init; } = "MIT";
    public string? WWWRootUrl { get; init; } = "gravity-files";
    public string? HomePage { get; init; } = "/gravity/";
    public string? HomePageDescription { get; init; } = L.T("봇 스폰·장비 설정", "Configure bot spawns and gear");
}

/// <summary>
/// 서버 콘솔 문구 언어(사용자 11-10): SPT 서버 언어(locale.json serverLocale, "system"이면 윈도우 언어)가 한국어면 한글, 아니면 영어.
/// GravityLoader가 SPT LocaleService로 정하기 전에는 윈도우 표시 언어로 고른다. 웹 화면은 따로 한/영 전환이 있다.
/// </summary>
public static class L
{
    public static bool English { get; private set; } = !CultureInfo.CurrentUICulture.Name.StartsWith("ko", StringComparison.OrdinalIgnoreCase);

    public static void Use(string? locale)
    {
        if (!string.IsNullOrWhiteSpace(locale)) English = !locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase);
    }

    public static string T(string ko, string en) => English ? en : ko;
}

public static class GravityInfo
{
    public const string Version = "1.0.4";
    public const string Tag = "[Gravity]";
}
