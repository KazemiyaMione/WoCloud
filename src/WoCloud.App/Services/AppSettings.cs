using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WoCloud.App.Services;

/// <summary>
/// 本地设置（下载目录/并发/续传等），保存在 data/settings.json，与会话文件同目录。
/// </summary>
public partial class AppSettings : ObservableObject
{
    private const string FileName = "settings.json";
    private static string? _resolvedDir;

    /// <summary>默认下载目录：用户「下载」文件夹。</summary>
    [ObservableProperty] private string _downloadFolder = DefaultDownloadFolder();

    /// <summary>同时下载的文件数（1-5）。</summary>
    [ObservableProperty] private int _downloadConcurrency = 3;

    /// <summary>断点续传（保留 .part，可续传）。</summary>
    [ObservableProperty] private bool _resumeDownload = true;

    /// <summary>上传/下载完成后自动刷新文件列表。</summary>
    [ObservableProperty] private bool _autoRefreshAfterTransfer = true;

    /// <summary>上传分片大小（MB）：8 / 16 / 32。</summary>
    [ObservableProperty] private int _uploadChunkMb = 8;

    /// <summary>上传并发（同时传几个分片）：1-12。服务端按连接限速，提高并发可叠加带宽。</summary>
    [ObservableProperty] private int _uploadConcurrency = 6;

    /// <summary>外观主题：0=跟随系统，1=浅色，2=深色。</summary>
    [ObservableProperty] private int _themeMode = 0;

    /// <summary>上次打卡日期（本地日期 yyyy-MM-dd）。</summary>
    [ObservableProperty] private string _lastCheckInDate = "";

    /// <summary>连续打卡天数。</summary>
    [ObservableProperty] private int _checkInStreak;

    /// <summary>累计打卡天数。</summary>
    [ObservableProperty] private int _checkInTotal;

    public static string DefaultDownloadFolder()
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(downloads)
            ? downloads
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    // ---------------- 持久化 ----------------

    private static string StoreDir
    {
        get
        {
            if (_resolvedDir is not null) return _resolvedDir;
            string[] candidates =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WoCloud"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WoCloud"),
                Path.Combine(AppContext.BaseDirectory, "data"),
            ];
            foreach (var dir in candidates)
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    var probe = Path.Combine(dir, ".probe");
                    File.WriteAllText(probe, "1");
                    File.Delete(probe);
                    _resolvedDir = dir;
                    return dir;
                }
                catch { /* 换下一个候选目录 */ }
            }
            _resolvedDir = AppContext.BaseDirectory;
            return _resolvedDir;
        }
    }

    private static string StorePath => Path.Combine(StoreDir, FileName);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s is not null) return s;
            }
        }
        catch { /* 损坏则回退默认值 */ }
        return new AppSettings();
    }

    public void Save()
    {
        try { File.WriteAllText(StorePath, JsonSerializer.Serialize(this)); }
        catch { /* 设置保存失败不影响使用 */ }
    }

    /// <summary>属性变化即落盘（简单可靠）。</summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not null and not nameof(DownloadFolderText)) Save();
    }

    public string DownloadFolderText => string.IsNullOrEmpty(DownloadFolder) ? "未设置" : DownloadFolder;
}
