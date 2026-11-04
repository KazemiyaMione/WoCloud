using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using WoCloud.Client.Files;
using WoCloud.Client.Models;
using WoCloud.Client.Transfer;

namespace WoCloud.App.Controls;

/// <summary>
/// 通用文件预览浮层（文件页 / 私密空间页共用）：
/// 图片可缩放平移旋转、视频音频播放、文本阅读，←/→ 切换同目录文件，Esc 关闭。
/// 窗口变小时内容自适应，关闭按钮始终固定在右上角。
/// </summary>
public sealed partial class FilePreviewOverlay : UserControl
{
    private IReadOnlyList<CloudFile> _items = [];
    private int _index;
    private string _spaceType = "0";
    private string? _familyId;
    private string? _psToken;
    private double _rotation;

    private static readonly string[] ImageExt = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".ico"];
    private static readonly string[] VideoExt = [".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".m4v", ".flv", ".ts"];
    private static readonly string[] AudioExt = [".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".wma", ".ape"];
    private static readonly string[] TextExt =
        [".txt", ".md", ".json", ".xml", ".log", ".csv", ".ini", ".yml", ".yaml", ".cs", ".js", ".ts", ".html", ".css", ".py", ".sql", ".bat", ".ps1", ".sh"];

    /// <summary>状态提示（下载/打开失败等），由宿主页面显示。</summary>
    public event Action<string>? StatusChanged;

    public FilePreviewOverlay()
    {
        InitializeComponent();
        Root.SizeChanged += (_, e) => ApplyToolbar(e.NewSize.Width);
    }

    public bool IsOpen => Root.Visibility == Visibility.Visible;

    /// <summary>打开预览。<paramref name="items"/> 为同目录可切换列表（自动跳过文件夹）。</summary>
    public async Task ShowAsync(IReadOnlyList<CloudFile> items, CloudFile current,
        string spaceType = "0", string? familyId = null, string? psToken = null)
    {
        _spaceType = string.IsNullOrEmpty(current.SpaceType) ? spaceType : current.SpaceType;
        _familyId = familyId;
        _psToken = psToken;

        var list = items.Where(i => !i.IsDirectory).ToList();
        if (list.Count == 0) list = [current];
        _items = list;
        _index = list.FindIndex(x => x.Id == current.Id);
        if (_index < 0) _index = 0;

        Root.Visibility = Visibility.Visible;
        Root.Focus(FocusState.Programmatic);
        await LoadCurrentAsync();
        ApplyToolbar(Root.ActualWidth); // 布局完成后再判定一次（首帧 ActualWidth 可能为 0）
    }

    public void Close()
    {
        try { PreviewMedia.MediaPlayer?.Pause(); } catch { /* 播放器可能已释放 */ }
        PreviewMedia.Source = null;
        PreviewImage.Source = null;
        PreviewText.Text = "";
        Root.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
        _rotation = 0;
    }

    private CloudFile Current => _items[_index];

    // ---------------- 加载 ----------------

    private async Task LoadCurrentAsync()
    {
        var file = Current;
        var ext = Path.GetExtension(file.Name).ToLowerInvariant();

        TitleText.Text = file.Name;
        SubText.Text = $"{file.SizeText} · {file.TimeText}";
        HintText.Text = _items.Count > 1 ? $"{_index + 1} / {_items.Count} · ←/→ 切换 · Ctrl+滚轮缩放" : "Ctrl+滚轮缩放";
        PrevBtn.IsEnabled = _items.Count > 1;
        NextBtn.IsEnabled = _items.Count > 1;

        PreviewImage.Visibility = Visibility.Collapsed;
        ImageScroll.Visibility = Visibility.Collapsed;
        PreviewMedia.Visibility = Visibility.Collapsed;
        TextScroll.Visibility = Visibility.Collapsed;
        InfoPanel.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        PreviewMedia.Source = null;
        PreviewText.Text = "";
        _rotation = 0;
        PreviewRotate.Angle = 0;
        LoadingRing.IsActive = true;

        try
        {
            if (file.FileType == "1" || ImageExt.Contains(ext))
            {
                var url = file.PreviewUrl is { Length: > 0 } p ? p : await ResolveUrlAsync(file);
                if (string.IsNullOrEmpty(url)) throw new InvalidOperationException("服务端未返回图片地址");

                // 自己下载字节再解码：远程 URL 直绑在部分 CDN/鉴权场景下会静默失败
                var bytes = await DownloadBytesAsync(url);
                if (bytes is null || bytes.Length == 0)
                    throw new InvalidOperationException("图片下载失败（服务端返回空内容）");

                var bmp = new BitmapImage();
                using (var ms = new MemoryStream(bytes))
                    await bmp.SetSourceAsync(ms.AsRandomAccessStream());

                PreviewImage.Source = bmp;
                PreviewImage.Visibility = Visibility.Visible;   // ← 之前漏了这行，导致只剩遮罩
                ImageScroll.Visibility = Visibility.Visible;
                _imgW = bmp.PixelWidth;
                _imgH = bmp.PixelHeight;
                SetImageToolsVisible(true);
                // 等布局完成后按窗口大小自适应缩放（和系统看图一样：先整图可见）
                DispatcherQueue.TryEnqueue(() =>
                {
                    ImageScroll.ChangeView(0, 0, 1, true);
                    FitToWindow();
                });
            }
            else if (file.FileType == "2" || VideoExt.Contains(ext))
            {
                var url = await ResolveUrlAsync(file) ?? throw new InvalidOperationException("服务端未返回视频地址");
                PreviewMedia.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(url));
                PreviewMedia.Visibility = Visibility.Visible;
                SetImageToolsVisible(false);
            }
            else if (file.FileType == "3" || AudioExt.Contains(ext))
            {
                var url = await ResolveUrlAsync(file) ?? throw new InvalidOperationException("服务端未返回音频地址");
                PreviewMedia.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(url));
                PreviewMedia.Visibility = Visibility.Visible;
                SetImageToolsVisible(false);
            }
            else if (TextExt.Contains(ext) || (file.FileType == "4" && file.Size < 1024 * 1024))
            {
                var url = await ResolveUrlAsync(file) ?? throw new InvalidOperationException("服务端未返回下载地址");
                using var http = new HttpClient();
                var text = await http.GetStringAsync(url);
                if (text.Length > 200_000) text = text[..200_000] + "\n\n…（已截断，仅预览前 200KB）";
                PreviewText.Text = text;
                TextScroll.Visibility = Visibility.Visible;
                SetImageToolsVisible(false);
            }
            else
            {
                InfoGlyph.Glyph = "\uE7C3";
                InfoText.Text = $"暂不支持内置预览（{ext}）\n{file.Name}\n\n可点下方「下载到…」或「用系统应用打开」";
                InfoPanel.Visibility = Visibility.Visible;
                SetImageToolsVisible(false);
            }
        }
        catch (Exception e)
        {
            InfoGlyph.Glyph = "\uE783";
            InfoText.Text = "预览失败：" + e.Message;
            InfoPanel.Visibility = Visibility.Visible;
            SetImageToolsVisible(false);
            StatusChanged?.Invoke("预览失败：" + e.Message);
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private bool _isImage;

    private void SetImageToolsVisible(bool visible)
    {
        _isImage = visible;
        ApplyToolbar(Root.ActualWidth);
    }

    /// <summary>窗口变窄时自动精简工具栏，保证「关闭 / 翻页」始终可见。</summary>
    private void ApplyToolbar(double width)
    {
        var compact = width < 820;
        ZoomInBtn.Visibility = _isImage && !compact ? Visibility.Visible : Visibility.Collapsed;
        ZoomOutBtn.Visibility = _isImage && !compact ? Visibility.Visible : Visibility.Collapsed;
        ZoomText.Visibility = _isImage && !compact ? Visibility.Visible : Visibility.Collapsed;
        FitBtn.Visibility = _isImage && !compact ? Visibility.Visible : Visibility.Collapsed;
        RotateBtn.Visibility = _isImage ? Visibility.Visible : Visibility.Collapsed;
        PrevBtn.Visibility = _items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.Visibility = _items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>下载到内存（预览用，超过 <paramref name="maxBytes"/> 时截断保护）。</summary>
    private static async Task<byte[]?> DownloadBytesAsync(string url, long maxBytes = 64L * 1024 * 1024)
    {
        using var http = new HttpClient();
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        var len = resp.Content.Headers.ContentLength;
        if (len > maxBytes) throw new InvalidOperationException($"图片过大（{len / 1048576} MB），请下载后查看");
        return await resp.Content.ReadAsByteArrayAsync();
    }

    private async Task<string?> ResolveUrlAsync(CloudFile file)
    {
        try
        {
            var api = App.Current.Services.GetRequiredService<FileApi>();
            var r = await api.GetDownloadUrlsAsync([file.Fid], null, _spaceType, _familyId, _psToken);
            return r?.FirstOrDefault()?.DownloadUrl;
        }
        catch (Exception e)
        {
            StatusChanged?.Invoke("获取预览地址失败：" + e.Message);
            return null;
        }
    }

    // ---------------- 交互 ----------------

    private async void Prev_Click(object sender, RoutedEventArgs e) => await StepAsync(-1);
    private async void Next_Click(object sender, RoutedEventArgs e) => await StepAsync(1);

    private async Task StepAsync(int delta)
    {
        if (_items.Count <= 1) return;
        _index = (_index + delta + _items.Count) % _items.Count;
        await LoadCurrentAsync();
    }

    private double _imgW;
    private double _imgH;

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(ImageScroll.ZoomFactor * 1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(ImageScroll.ZoomFactor / 1.25);
    private void Fit_Click(object sender, RoutedEventArgs e) => FitToWindow();

    /// <summary>缩放到整图刚好填满可视区域（不放大超过 100%）。</summary>
    private void FitToWindow()
    {
        var vw = ImageScroll.ViewportWidth;
        var vh = ImageScroll.ViewportHeight;
        if (vw <= 0 || vh <= 0 || _imgW <= 0 || _imgH <= 0) return;
        var zoom = Math.Clamp(Math.Min(vw / _imgW, vh / _imgH), 0.2, 1.0);
        ImageScroll.ChangeView(0, 0, (float)zoom, true);
    }

    private void SetZoom(double factor)
    {
        factor = Math.Clamp(factor, 0.2, 8);
        ImageScroll.ChangeView(null, null, (float)factor, false);
    }

    private void ImageScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        => ZoomText.Text = $"{ImageScroll.ZoomFactor * 100:F0}%";

    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        _rotation = (_rotation + 90) % 360;
        PreviewRotate.Angle = _rotation;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Root_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Root)) Close();
    }

    private async void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Escape:
                Close();
                break;
            case Windows.System.VirtualKey.Left when PreviewMedia.Visibility != Visibility.Visible:
                await StepAsync(-1);
                break;
            case Windows.System.VirtualKey.Right when PreviewMedia.Visibility != Visibility.Visible:
                await StepAsync(1);
                break;
            case Windows.System.VirtualKey.Add:
                SetZoom(ImageScroll.ZoomFactor * 1.25);
                break;
            case Windows.System.VirtualKey.Subtract:
                SetZoom(ImageScroll.ZoomFactor / 1.25);
                break;
            case Windows.System.VirtualKey.Number0:
                SetZoom(1);
                break;
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is null) return;
        try
        {
            var downloads = App.Current.Services.GetRequiredService<DownloadClient>();
            await downloads.DownloadAsync(Current.Fid, Path.Combine(folder.Path, Current.Name),
                spaceType: _spaceType, familyId: _familyId, psToken: _psToken);
            StatusChanged?.Invoke("下载完成：" + Current.Name);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("下载失败：" + ex.Message);
        }
    }

    private async void OpenExternal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var temp = Path.Combine(Path.GetTempPath(), "WoCloud", "preview");
            Directory.CreateDirectory(temp);
            // 与 DownloadClient 落盘清洗保持一致，否则文件名带非法字符时打开找不到文件
            var target = Path.Combine(temp, DownloadClient.SanitizeFileName(Current.Name));
            var downloads = App.Current.Services.GetRequiredService<DownloadClient>();
            await downloads.DownloadAsync(Current.Fid, target,
                spaceType: _spaceType, familyId: _familyId, psToken: _psToken);
            var sf = await Windows.Storage.StorageFile.GetFileFromPathAsync(target);
            await Windows.System.Launcher.LaunchFileAsync(sf);
            StatusChanged?.Invoke("已交给系统默认应用打开");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("打开失败：" + ex.Message);
        }
    }

    private static async Task<Windows.Storage.StorageFolder?> PickFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        if (App.MainWin is not null)
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWin));
        return await picker.PickSingleFolderAsync();
    }
}
