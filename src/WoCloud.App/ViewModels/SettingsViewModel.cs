using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WoCloud.App.Services;
using WoCloud.Client.Auth;
using WoCloud.Client.Core;
using WoCloud.Client.Member;

namespace WoCloud.App.ViewModels;

/// <summary>设置页：下载目录/并发/续传、上传分片、会话与退出登录。</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SessionState _session;
    private readonly AuthApi _auth;
    private readonly MemberApi _member;
    private readonly ShellNav _nav;
    private readonly FileListViewModel _files;

    public AppSettings Settings => _settings;

    public ObservableCollection<int> ConcurrencyOptions { get; } = [1, 2, 3, 4, 5];
    public ObservableCollection<int> ChunkOptions { get; } = [8, 16, 32];

    /// <summary>外观：0 跟随系统 / 1 浅色 / 2 深色。</summary>
    public ObservableCollection<string> ThemeOptions { get; } = ["跟随系统", "浅色", "深色"];

    [ObservableProperty] private int _themeIndex;

    [ObservableProperty] private string _accountText = "未登录";
    [ObservableProperty] private string _statusText = "";

    public string VersionText { get; } =
        typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    private readonly WoCloud.Client.Transfer.UploadClient _uploads;
    private readonly WoCloud.Client.Transfer.DownloadClient _downloads;

    public SettingsViewModel(AppSettings settings, SessionState session, AuthApi auth,
        MemberApi member, ShellNav nav, FileListViewModel files,
        WoCloud.Client.Transfer.UploadClient uploads, WoCloud.Client.Transfer.DownloadClient downloads)
    {
        _settings = settings;
        _session = session;
        _auth = auth;
        _member = member;
        _nav = nav;
        _files = files;
        _uploads = uploads;
        _downloads = downloads;

        _themeIndex = _settings.ThemeMode;

        _settings.PropertyChanged += (_, _) => Apply();
        Apply();
    }

    partial void OnThemeIndexChanged(int value)
    {
        _settings.ThemeMode = value;   // 触发 Apply() → 落盘 + 应用主题
    }

    /// <summary>把设置同步到传输客户端（分片大小/并发/续传）与主题。</summary>
    private void Apply()
    {
        _uploads.ChunkSizeBytes = Math.Max(1, _settings.UploadChunkMb) * 1024 * 1024;
        _downloads.Concurrency = Math.Clamp(_settings.DownloadConcurrency, 1, 5);
        _downloads.ResumeEnabled = _settings.ResumeDownload;
        WoCloud.App.App.ApplyTheme(_settings.ThemeMode);
    }

    /// <summary>读取账号信息（昵称 + 会员等级 + 空间）。</summary>
    public async Task LoadAsync()
    {
        if (!_session.IsLoggedIn)
        {
            AccountText = "未登录";
            return;
        }
        try
        {
            var user = await _auth.GetUserInfoAsync();
            var summary = await _member.GetMemberSummaryAsync();
            AccountText = summary is null
                ? $"{user?.UserName}（{user?.UserId}）"
                : $"{user?.UserName}（{user?.UserId}） · {summary.LevelText} · {summary.UsedText}/{summary.AllText}";
        }
        catch (Exception e)
        {
            AccountText = "账号信息获取失败：" + e.Message;
        }
    }

    /// <summary>退出登录：清本地会话 → 回登录页。</summary>
    public async Task LogoutAsync()
    {
        try { await _auth.LogoutAsync(); }
        catch { /* 服务端登出失败也要清本地 */ }
        finally
        {
            _session.Clear();
            _files.Files.Clear();
            _files.TransferItems.Clear();
            _nav.NavigateToLogin();
        }
    }

    /// <summary>删除本地会话/设置文件。</summary>
    public void ClearLocalData()
    {
        try
        {
            _session.DeleteStore();
            StatusText = "本地缓存已清除";
        }
        catch (Exception e) { StatusText = "清除失败：" + e.Message; }
    }
}
