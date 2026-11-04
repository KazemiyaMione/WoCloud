using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WoCloud.App.Services;
using WoCloud.App.Views;

namespace WoCloud.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "WoCloud 联通云盘";
        // Mica 云母材质
        SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
        ExtendsContentIntoTitleBar = true;
        // WinUI 3 窗口尺寸只能在代码里设置
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(1180, 760));

        // 窗口/任务栏图标（exe 图标由 csproj ApplicationIcon 提供，这里补运行时窗口图标）
        try
        {
            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(icon)) appWindow.SetIcon(icon);
        }
        catch
        {
            // 图标缺失不影响启动
        }
    }

    // ---------------- 右上角个人信息 ----------------

    private bool _userLoaded;

    /// <summary>拉取头像/昵称/会员等级并刷新右上角用户区（登录后调用）。</summary>
    public async void RefreshUserChip()
    {
        try
        {
            var services = App.Current.Services;
            var user = await services.GetRequiredService<WoCloud.Client.Auth.AuthApi>().GetUserInfoAsync();
            if (user is null || string.IsNullOrEmpty(user.UserId))
            {
                UserChip.Visibility = Visibility.Collapsed;
                _userLoaded = false;
                return;
            }

            UserNameText.Text = string.IsNullOrWhiteSpace(user.UserName) ? "我的账号" : user.UserName;

            var bmp = await LoadBitmapAsync(user.HeadUrl);
            if (bmp is not null)
            {
                AvatarImage.Source = bmp;
                AvatarImage.Visibility = Visibility.Visible;
                AvatarFallback.Visibility = Visibility.Collapsed;
            }
            else
            {
                AvatarImage.Visibility = Visibility.Collapsed;
                AvatarFallback.Visibility = Visibility.Visible;
            }

            try
            {
                var summary = await services.GetRequiredService<WoCloud.Client.Member.MemberApi>()
                    .GetMemberSummaryAsync();
                LevelTextBlock.Text = summary is null
                    ? "普通用户"
                    : $"{summary.LevelText} · {summary.UsedText}/{summary.AllText}";
            }
            catch { LevelTextBlock.Text = ""; }

            UserChip.Visibility = Visibility.Visible;
            _userLoaded = true;
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine("用户信息加载失败: " + e.Message);
            UserChip.Visibility = Visibility.Collapsed;
        }
    }

    private static async Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> LoadBitmapAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            using var http = new HttpClient();
            var bytes = await http.GetByteArrayAsync(url);
            var ms = new MemoryStream(bytes);
            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            await bmp.SetSourceAsync(ms.AsRandomAccessStream());
            return bmp;
        }
        catch
        {
            return null; // 头像失败不影响主界面
        }
    }

    private void UserChip_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        => Navigate<MemberPage>();

    /// <summary>登录页期间屏蔽导航栏的 SelectionChanged（否则初始选中项会把登录页顶掉）。</summary>
    private bool _suppressNav;

    public void Navigate<TPage>() where TPage : Page
    {
        var tag = typeof(TPage).Name switch
        {
            nameof(FileListPage) => "files",
            nameof(TransferPage) => "transfer",
            nameof(SharePage) => "share",
            nameof(FlyPassPage) => "flypass",
            nameof(RecyclePage) => "recycle",
            nameof(PrivateSpacePage) => "private",
            nameof(LabPage) => "lab",
            nameof(SettingsPage) => "settings",
            nameof(MemberPage) => "member",
            _ => null,
        };
        // 登录页：整页显示，隐藏导航栏与用户区（博客式登录页）
        var isLogin = typeof(TPage) == typeof(LoginPage);
        _suppressNav = isLogin;
        Nav.IsPaneVisible = !isLogin;
        Nav.IsPaneToggleButtonVisible = !isLogin;

        // 首次登录进入主界面时先弹免责声明（同意过则不再打扰）
        if (!isLogin) EnsureDisclaimer();

        // 登录页不显示在导航选择中
        if (tag is null)
        {
            Nav.SelectedItem = null;
            UserChip.Visibility = Visibility.Collapsed;
            _userLoaded = false;
            ContentFrame.Navigate(typeof(TPage));
            return;
        }

        if (!_userLoaded) RefreshUserChip(); // 登录后首次进入主界面时拉取头像/等级
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>()
                     .Concat(Nav.FooterMenuItems.OfType<NavigationViewItem>()))
        {
            if ((string)item.Tag == tag)
            {
                Nav.SelectedItem = item;
                return;
            }
        }
        ContentFrame.Navigate(typeof(TPage));
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // 登录页/未登录时忽略：NavigationView 加载完会对初始选中项补发一次事件
        if (_suppressNav || !Nav.IsPaneVisible) return;
        if (args.SelectedItem is not NavigationViewItem item) return;
        ContentFrame.Navigate((string)item.Tag switch
        {
            "files" => typeof(FileListPage),
            "transfer" => typeof(TransferPage),
            "share" => typeof(SharePage),
            "flypass" => typeof(FlyPassPage),
            "recycle" => typeof(RecyclePage),
            "private" => typeof(PrivateSpacePage),
            "lab" => typeof(LabPage),
            "settings" => typeof(SettingsPage),
            "member" => typeof(MemberPage),
            _ => typeof(FileListPage),
        });
    }

    // 退出登录已移入「设置」页（含二次确认）

    // ---------------- 免责声明 ----------------

    private bool _disclaimerBusy;
    private int _disclaimerRetry;

    /// <summary>首次进入主界面时弹免责声明；不同意直接退出程序。</summary>
    private async void EnsureDisclaimer()
    {
        if (_disclaimerBusy) return;

        var settings = App.Current.Services.GetRequiredService<AppSettings>();
        if (settings.DisclaimerVersion >= AppInfo.DisclaimerVersion) return;

        var root = ContentFrame.XamlRoot;
        if (root is null)
        {
            // 窗口还没完成首帧布局，稍后再试（最多约 2 秒）
            if (_disclaimerRetry++ < 20)
            {
                ContentFrame.DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(100);
                    EnsureDisclaimer();
                });
            }
            return;
        }

        _disclaimerBusy = true;
        try
        {
            if (await DisclaimerDialog.ShowAsync(root, requireConsent: true))
            {
                settings.DisclaimerVersion = AppInfo.DisclaimerVersion;   // 自动落盘
            }
            else
            {
                Application.Current.Exit();
            }
        }
        finally
        {
            _disclaimerBusy = false;
        }
    }

}

