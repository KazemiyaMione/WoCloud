using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WoCloud.App.Services;
using WoCloud.App.Views;
using WoCloud.Client.Auth;
using WoCloud.Client.Core;

namespace WoCloud.App;

public partial class App : Application
{
    public new static App Current => (App)Application.Current;
    public static Window? MainWin { get; private set; }
    public IServiceProvider Services { get; }
    private Window? _mainWindow;

    public App()
    {
        Services = AppServices.Build();
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            System.Diagnostics.Debug.WriteLine("Unhandled: " + e.Message);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        MainWin = _mainWindow;
        _mainWindow.Activate();

        // 应用已保存的主题（0 跟随系统 / 1 浅色 / 2 深色）
        ApplyTheme(Services.GetRequiredService<AppSettings>().ThemeMode);

        // 会话失效 → 回到登录页
        Services.GetRequiredService<SessionState>().SessionExpired += () =>
        {
            _mainWindow?.DispatcherQueue.TryEnqueue(() =>
                Services.GetRequiredService<ShellNav>().NavigateToLogin());
        };

        var nav = Services.GetRequiredService<ShellNav>();
        nav.Initialize(_mainWindow);
        if (Services.GetRequiredService<SessionState>().TryRestore())
        {
            nav.NavigateToFiles();
            _ = ValidateSessionAsync(nav); // 后台校验 token，过期则回登录页
        }
        else
        {
            nav.NavigateToLogin();
        }
    }

    /// <summary>
    /// 运行时切换主题：对窗口根元素设置 RequestedTheme（Application.RequestedTheme 启动后不可改）。
    /// </summary>
    /// <param name="mode">0=跟随系统，1=浅色，2=深色</param>
    public static void ApplyTheme(int mode)
    {
        if (MainWin?.Content is not FrameworkElement root) return;

        root.RequestedTheme = mode switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        // 标题栏按钮颜色跟随（Mica 背景下深色主题要用浅色字形）
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWin);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var titleBar = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId).TitleBar;

            var isDark = mode == 2 || (mode == 0 && Application.Current.RequestedTheme == ApplicationTheme.Dark);

            if (titleBar is not null)
            {
                var fg = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
                titleBar.ButtonForegroundColor = fg;
                titleBar.ButtonHoverForegroundColor = fg;
                titleBar.ButtonPressedForegroundColor = fg;
            }

            // Mica / 标题栏的明暗由窗口的沉浸式深色属性决定（不设的话深色内容会压在浅色毛玻璃上）
            var darkFlag = isDark ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkFlag, sizeof(int));
        }
        catch
        {
            // 标题栏着色失败不影响主题本体
        }
    }

    private const int DwmwaUseImmersiveDarkMode = 20;

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>启动时校验持久化的 token 是否仍有效，避免带着过期 token 进主界面报 1001。</summary>
    private async Task ValidateSessionAsync(ShellNav nav)
    {
        try
        {
            var user = await Services.GetRequiredService<AuthApi>().GetUserInfoAsync();
            if (user is null) nav.NavigateToLogin();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("会话校验失败: " + ex.Message);
            nav.NavigateToLogin();
        }
    }
}
