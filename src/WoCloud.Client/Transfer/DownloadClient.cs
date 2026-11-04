using WoCloud.Client.Files;

namespace WoCloud.Client.Transfer;

/// <summary>下载进度事件参数。</summary>
public sealed class DownloadProgressEventArgs(string fid, string fileName, long received, long total, double speedBps)
    : EventArgs
{
    public string Fid { get; } = fid;
    public string FileName { get; } = fileName;
    public long Received { get; } = received;
    public long Total { get; } = total;
    public double Progress => Total > 0 ? (double)Received / Total : 0;
    public double SpeedBps { get; } = speedBps;
}

/// <summary>
/// CDN 直链下载器：流式拷贝 + Range 断点续传。
/// </summary>
public sealed class DownloadClient : IDisposable
{
    private const int ChunkSize = 8 * 1024 * 1024; // 8MB

    private readonly HttpClient _http;
    private readonly FileApi _files;

    public event EventHandler<DownloadProgressEventArgs>? Progress;

    /// <summary>批量下载并发数（设置页可调，1~5）。</summary>
    public int Concurrency { get; set; } = 3;

    /// <summary>是否启用断点续传（关闭时忽略并删除 .part）。</summary>
    public bool ResumeEnabled { get; set; } = true;

    public DownloadClient(FileApi files, HttpClient? httpClient = null)
    {
        _files = files;
        _http = httpClient ?? new HttpClient();
    }

    /// <summary>
    /// 下载单个文件到目标路径（自动断点续传：已有 .part 文件时从断点 Range 续传）。
    /// 返回实际落盘的完整路径（正常情况与传入一致；文件名带 Windows 存不了的字符时才有差异）。
    /// </summary>
    public async Task<string> DownloadAsync(string fid, string targetPath,
        IProgress<double>? progress = null, CancellationToken ct = default,
        string spaceType = "0", string? familyId = null, string? psToken = null)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
        var fileName = Path.GetFileName(targetPath);

        try
        {
            return await DownloadCoreAsync(fid, dir, fileName, progress, ct, spaceType, familyId, psToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or NotSupportedException &&
                                   fileName != SanitizeFileName(fileName))
        {
            // 云端文件名带 Windows 无法存储的字符（结尾点/空格 → 「拒绝访问」，\ / : * ? " < > | →
            // NotSupportedException）。正常文件名永远不会走到这里；走到才做最小调整。
            return await DownloadCoreAsync(fid, dir, SanitizeFileName(fileName), progress, ct, spaceType, familyId, psToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<string> DownloadCoreAsync(string fid, string dir, string fileName,
        IProgress<double>? progress, CancellationToken ct,
        string spaceType, string? familyId, string? psToken)
    {
        var urls = await _files.GetDownloadUrlsAsync([fid], null, spaceType, familyId, psToken, ct)
                       .ConfigureAwait(false)
                   ?? throw new InvalidOperationException("获取下载直链失败");
        if (urls.Count == 0)
            throw new InvalidOperationException("下载直链列表为空");
        var url = urls[0].DownloadUrl;
        var targetPath = Path.Combine(dir, fileName);

        Directory.CreateDirectory(dir);
        var partPath = targetPath + ".part";
        if (!ResumeEnabled)
        {
            try { if (File.Exists(partPath)) File.Delete(partPath); } catch { /* 忽略 */ }
        }

        // 残留 .part 若带隐藏/只读属性，FileMode.Create/Append 会直接「拒绝访问」——先归位
        try { if (File.Exists(partPath)) File.SetAttributes(partPath, FileAttributes.Normal); }
        catch { /* 属性清不掉就交给下面的兜底 */ }

        long existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        // 断点续传：Range 被拒（416/404，通常是 .part 已完整或链接换签）时清空重下
        HttpResponseMessage resp;
        if (existing > 0)
        {
            resp = await GetWithRangeAsync(url, existing, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                resp.Dispose();
                try { File.Delete(partPath); } catch { /* 忽略删除失败 */ }
                existing = 0;
                resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            }
        }
        else
        {
            resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }

        using var _respScope = resp;
        resp.EnsureSuccessStatusCode();

        long total = existing + (resp.Content.Headers.ContentLength ?? 0);
        var mode = existing > 0 ? FileMode.Append : FileMode.Create;

        // 必须先把文件流关掉再 Move，否则 Windows 报「文件被另一进程占用」
        FileStream fs;
        try
        {
            fs = new FileStream(partPath, mode, FileAccess.Write, FileShare.None, ChunkSize, useAsync: true);
        }
        catch (UnauthorizedAccessException)
        {
            // .part 被别的进程占用或删除挂起：换唯一名兜底，绝不因残留文件让下载失败
            partPath = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:HHmmssfff}.part");
            existing = 0;
            fs = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, ChunkSize, useAsync: true);
        }

        await using (fs)
        {
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            var buffer = new byte[ChunkSize];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long received = existing;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                received += read;
                var speed = sw.Elapsed.TotalSeconds > 0.5 ? received / sw.Elapsed.TotalSeconds : 0;
                Progress?.Invoke(this, new DownloadProgressEventArgs(fid, fileName, received, total, speed));
                progress?.Report(total > 0 ? (double)received / total : 0);
            }
        }

        try
        {
            File.Move(partPath, targetPath, overwrite: true);
        }
        catch (UnauthorizedAccessException)
        {
            // 目标文件已存在且只读/隐藏：清属性后重试，成功后 .part 随之消失
            try { if (File.Exists(targetPath)) File.SetAttributes(targetPath, FileAttributes.Normal); }
            catch { /* 保持原错误 */ }
            File.Move(partPath, targetPath, overwrite: true);
        }

        return targetPath;
    }

    /// <summary>批量下载（按 <see cref="Concurrency"/> 并发）。</summary>
    public async Task DownloadBatchAsync(IEnumerable<(string Fid, string TargetPath)> items,
        IProgress<(string Fid, double Progress)>? progress = null, CancellationToken ct = default)
    {
        using var gate = new SemaphoreSlim(Math.Clamp(Concurrency, 1, 5));
        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var inner = new Progress<double>(p => progress?.Report((item.Fid, p)));
                await DownloadAsync(item.Fid, item.TargetPath, inner, ct).ConfigureAwait(false);
            }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> GetWithRangeAsync(string url, long offset, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Get, url);
        msg.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);
        var resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // 已下完：把 .part 改名后抛取消
            resp.Dispose();
            throw new InvalidOperationException("断点已超过文件大小，请删除 .part 后重试");
        }
        return resp;
    }

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// 清洗文件名使其可安全落盘：替换 Windows 非法字符（<c>\ / : * ? " &lt; &gt; |</c> 及控制符），
    /// 去掉结尾的点/空格（这两者不会被 GetInvalidFileNameChars 命中，但 FileStream 会报「拒绝访问」）。
    /// </summary>
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var buf = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            buf[i] = Array.IndexOf(invalid, c) >= 0 || char.IsControl(c) ? '_' : c;
        }
        var cleaned = new string(buf).TrimEnd('.', ' ');
        return cleaned.Length > 0 ? cleaned : "未命名文件";
    }
}
