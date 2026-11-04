namespace WoCloud.App.Services;

/// <summary>应用元信息：关于页、免责声明共用同一份，避免多处硬编码。</summary>
public static class AppInfo
{
    public const string Name = "WoCloud";
    public const string Title = "WoCloud · 联通云盘第三方客户端";
    public const string RepoUrl = "https://github.com/KazemiyaMione/WoCloud";
    public const string IssuesUrl = RepoUrl + "/issues";
    public const string ReleasesUrl = RepoUrl + "/releases";
    public const string LicenseUrl = RepoUrl + "/blob/master/LICENSE";
    public const string NoticeUrl = RepoUrl + "/blob/master/NOTICE";
    public const string License = "MIT";

    /// <summary>免责声明版本：条款有实质修改时 +1，已同意的用户下次启动会重新弹窗。</summary>
    public const int DisclaimerVersion = 1;

    /// <summary>第三方组件（名称 + 许可证），关于页直接展示。</summary>
    public const string ThirdParty =
        "Microsoft Windows App SDK（MIT）、CommunityToolkit.Mvvm（MIT）、" +
        "Microsoft.Extensions.DependencyInjection（MIT）";

    public static string Version { get; } =
        "v" + (typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
}
