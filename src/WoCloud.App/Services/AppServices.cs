using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using WoCloud.App.ViewModels;
using WoCloud.App.Views;
using WoCloud.Client.Auth;
using WoCloud.Client.Core;
using WoCloud.Client.Files;
using WoCloud.Client.Member;
using WoCloud.Client.Misc;
using WoCloud.Client.Share;
using WoCloud.Client.Space;
using WoCloud.Client.Transfer;

namespace WoCloud.App.Services;

/// <summary>DI 组合根（与 ConsoleTest 一致 + ViewModel）。</summary>
public static class AppServices
{
    public static IServiceProvider Build()
    {
        var sc = new ServiceCollection();
        var session = new SessionState();
        sc.AddSingleton(session);
        sc.AddSingleton<DispatcherClient>();
        sc.AddSingleton<AuthApi>();
        sc.AddSingleton<AccountApi>();
        sc.AddSingleton<FileApi>();
        sc.AddSingleton<DownloadClient>();
        sc.AddSingleton<UploadClient>();
        sc.AddSingleton<ShareApi>();
        sc.AddSingleton<MemberApi>();
        sc.AddSingleton<RecycleApi>();
        sc.AddSingleton<FlyPassApi>();
        sc.AddSingleton<PrivateSpaceApi>();
        sc.AddSingleton<MiscApi>();
        sc.AddSingleton<AppSettings>(_ => AppSettings.Load());
        sc.AddSingleton<TransferQueue>();
        sc.AddSingleton<ShellNav>();
        sc.AddSingleton<LoginViewModel>();
        sc.AddSingleton<FileListViewModel>();
        sc.AddSingleton<TransferViewModel>();
        sc.AddSingleton<ShareViewModel>();
        sc.AddSingleton<MemberViewModel>();
        sc.AddSingleton<RecycleViewModel>();
        sc.AddSingleton<FlyPassViewModel>();
        sc.AddSingleton<PrivateSpaceViewModel>();
        sc.AddSingleton<LabViewModel>();
        sc.AddSingleton<SettingsViewModel>();
        return sc.BuildServiceProvider();
    }
}

/// <summary>壳导航服务（页面 → Frame 跳转的桥）。</summary>
public sealed class ShellNav
{
    private Window? _window;
    private MainWindow? Shell => _window as MainWindow;

    public void Initialize(Window window) => _window = window;

    public void NavigateToLogin() => Shell?.Navigate<LoginPage>();
    public void NavigateToFiles() => Shell?.Navigate<FileListPage>();
    public void NavigateToTransfer() => Shell?.Navigate<TransferPage>();
    public void NavigateToShare() => Shell?.Navigate<SharePage>();
    public void NavigateToMember() => Shell?.Navigate<MemberPage>();
    public void NavigateToRecycle() => Shell?.Navigate<RecyclePage>();
    public void NavigateToFlyPass() => Shell?.Navigate<FlyPassPage>();
    public void NavigateToSettings() => Shell?.Navigate<SettingsPage>();
}
