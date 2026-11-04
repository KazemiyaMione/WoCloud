using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WoCloud.App.Services;
using WoCloud.App.ViewModels;

namespace WoCloud.App.Views;

public sealed partial class SettingsPage : Page, INotifyPropertyChanged
{
    public SettingsViewModel Vm { get; } = App.Current.Services.GetRequiredService<SettingsViewModel>();

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsPage()
    {
        InitializeComponent();
        Vm.PropertyChanged += (_, _) => RaiseAll();
        _ = Vm.LoadAsync();
    }

    public bool HasStatus => !string.IsNullOrEmpty(Vm.StatusText);

    private void RaiseAll()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    private async void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        if (App.MainWin is not null)
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWin));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        Vm.Settings.DownloadFolder = folder.Path;
        Vm.Settings.Save();
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "退出登录",
            Content = "退出后需要重新扫码或短信登录，确定继续吗？",
            PrimaryButtonText = "退出登录",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            await Vm.LogoutAsync();
    }

    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "清除本地缓存",
            Content = "将删除本地保存的登录会话（不会影响云端文件）。",
            PrimaryButtonText = "清除",
            CloseButtonText = "取消",
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            Vm.ClearLocalData();
    }

    // ---------------- 账号设置 ----------------

    private async void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ProfileDialog { XamlRoot = XamlRoot };
        await dlg.ShowAsync();
        if (!dlg.Saved) return;

        (App.MainWin as MainWindow)?.RefreshUserChip();
        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "个人资料",
            Content = "已保存，昵称与头像已同步到账号。",
            CloseButtonText = "知道了",
        }.ShowAsync();
    }

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PasswordDialog { XamlRoot = XamlRoot };
        await dlg.ShowAsync();
        if (!dlg.Changed) return;

        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "密码已修改",
            Content = "为保证账号安全，需要重新登录。",
            CloseButtonText = "去登录",
        }.ShowAsync();

        await Vm.LogoutAsync();
    }

    // ---------------- 关于 / 免责声明 ----------------

    private static async void OpenUrl(string url)
    {
        try { await Windows.System.Launcher.LaunchUriAsync(new Uri(url)); }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine($"打开链接失败 {url}: {e.Message}"); }
    }

    private void OpenRepo_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.RepoUrl);

    private void OpenIssues_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.IssuesUrl);

    private void OpenReleases_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.ReleasesUrl);

    private void OpenLicense_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.LicenseUrl);

    private void OpenNotice_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.NoticeUrl);

    private async void ShowDisclaimer_Click(object sender, RoutedEventArgs e) =>
        await DisclaimerDialog.ShowAsync(XamlRoot, requireConsent: false);
}
