using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using WoCloud.Client.Core;
using WoCloud.Client.Files;

namespace WoCloud.Client.Transfer;

/// <summary>上传进度事件参数。</summary>
public sealed class UploadProgressEventArgs(string fileName, long sent, long total, string state)
    : EventArgs
{
    public string FileName { get; } = fileName;
    public long Sent { get; } = sent;
    public long Total { get; } = total;
    public string State { get; } = state; // Uploading / Completed / Failed / Paused
    public double Progress => Total > 0 ? (double)Sent / Total : 0;
}

/// <summary>
/// 分片上传客户端（对齐前端 vue-simple-uploader 行为）：
/// 端点 = GetZoneInfo 返回的 {url}/openapi/client/upload2C；8MB 分片；并发 3；失败重试 1 次；
/// uniqueId 断点续传键；fileInfo 为 AES 密文。
/// </summary>
public sealed class UploadClient : IDisposable
{
    public const int DefaultChunkSize = 8 * 1024 * 1024; // 8388608（前端默认值）
    public const int MaxConcurrency = 3;
    public const int MaxRetries = 1;

    /// <summary>分片大小（设置页可调：8/16/32 MB）。</summary>
    public int ChunkSizeBytes { get; set; } = DefaultChunkSize;

    /// <summary>同时在传的分片数。实测服务端按「上传任务」整体限速（同一 uniqueId 的分片共享额度），
    /// 单文件内加大并发无效；真正的提速手段是多文件并行（见 FileParallelism）。</summary>
    public int Concurrency { get; set; } = MaxConcurrency;

    /// <summary>同时上传的文件数。限速按任务计，多文件并行可线性叠加吞吐（实测 2 文件并行 ≈ 2 倍速）。</summary>
    public int FileParallelism { get; set; } = 3;

    private readonly HttpClient _http;
    private readonly SessionState _session;
    private readonly FileApi _files;

    public event EventHandler<UploadProgressEventArgs>? Progress;

    public UploadClient(SessionState session, FileApi files, HttpClient? httpClient = null)
    {
        _session = session;
        _files = files;
        // 默认 100 秒对慢速上行太短：8MB 分片在 0.1MB/s 下就会触发超时，
        // 大文件直接废掉。这里放宽到 10 分钟，真正的取消交给 CancellationToken。
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    /// <summary>
    /// 上传一个本地文件到指定目录。
    /// </summary>
    /// <param name="localPath">本地文件路径</param>
    /// <param name="directoryId">目标目录 ID（根目录传 "0"）</param>
    /// <param name="uniqueId">断点续传键（默认 = 文件名_大小_最后修改时间 的 MD5 前缀）</param>
    /// <param name="fileType">fileType 分类（1图2视频3音4文档5其他，默认其他）</param>
    public async Task UploadAsync(string localPath, string directoryId,
        string? uniqueId = null, string? fileType = null,
        IProgress<double>? progress = null, CancellationToken ct = default,
        string? psToken = null, string spaceType = "0", string? familyId = null)
    {
        if (!_session.IsLoggedIn) throw new InvalidOperationException("未登录");
        var fi = new FileInfo(localPath) ?? throw new FileNotFoundException(localPath);
        var fileName = fi.Name;                       // 云端文件名 = 本地原文件名
        uniqueId ??= ComputeUniqueId();               // 前端同款：时间戳_随机串
        fileType ??= GuessFileType(fileName);         // 1图 2视频 3音频 4文档 5其他
        var batchNo = ComputeBatchNo();               // 前端同款：32 位随机串（缺它会返回 1000）

        var zone = await _files.GetZoneInfoAsync(ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("获取上传网关失败");

        // 上传节点选择（实测 2026-11，同账号 1MB 分片计时）：
        //   hyupload.pan.wo.cn（手机端 App 用的网关）  ~33s
        //   GetZoneInfo 分配的节点（如 bjupload…32443）~65s（约慢一倍）
        //   du.smartont.net:8443                       403 已下线
        // 手机端网关与网页端 upload2C 协议完全兼容（同参数直接 200），故优先走手机端，
        // 失败时自动回退分配节点；两端存储后端相同（分片可跨节点续传）。
        var endpoints = new List<string> { "https://hyupload.pan.wo.cn/openapi/client/upload2C" };
        if (!string.IsNullOrEmpty(zone.Url))
        {
            var zoneEndpoint = zone.Url.TrimEnd('/') + "/openapi/client/upload2C";
            if (!endpoints.Contains(zoneEndpoint)) endpoints.Add(zoneEndpoint);
        }

        var totalChunks = Math.Max((int)Math.Ceiling((double)fi.Length / ChunkSizeBytes), 1);
        // 字段顺序由服务端按位置解析，不能调整：
        // {"spaceType":"0","directoryId":"0","batchNo":"<32位>","fileName":"x.png","fileSize":123,"fileType":"1"}
        // 家庭云（spaceType=1）必须带 familyId，缺失服务端直接 HTTP 500（2026-11 实测）
        var fileInfoObj = new Dictionary<string, object>
        {
            ["spaceType"] = spaceType,
            ["directoryId"] = directoryId,
            ["batchNo"] = batchNo,
            ["fileName"] = fileName,
            ["fileSize"] = fi.Length,
            ["fileType"] = fileType,
        };
        if (familyId is not null) fileInfoObj["familyId"] = familyId;
        var fileInfoJson = System.Text.Json.JsonSerializer.Serialize(fileInfoObj);
        var fileInfoEnc = CryptoHelper.Encrypt(fileInfoJson, _session.AccessToken!);

        long done = 0;              // 已完成分片的字节数（原子累加）
        var lastEmit = -1L;
        var emitLock = new object();

        void Emit(long value, string state)
        {
            var clamped = Math.Clamp(value, 0, fi.Length);
            lock (emitLock)
            {
                if (clamped <= lastEmit && state != "Completed") return; // 并发下只前进不后退
                lastEmit = clamped;
            }
            Progress?.Invoke(this, new UploadProgressEventArgs(fileName, clamped, fi.Length, state));
            progress?.Report(fi.Length > 0 ? (double)clamped / fi.Length : 1);
        }

        // 真并发上传：多个 worker 抢队列里的分片。原实现用信号量包住整个请求，
        // 实际仍是一片一片串行发，慢速上行时吞吐只有单连接水平。
        var pending = new System.Collections.Concurrent.ConcurrentQueue<int>(
            Enumerable.Range(1, totalChunks));
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var errorLock = new object();
        Exception? firstError = null;

        async Task WorkerAsync()
        {
            try
            {
                while (!abort.IsCancellationRequested && pending.TryDequeue(out var part))
                {
                    var (start, length) = GetChunkRange(fi.Length, part);
                    var baseSent = Interlocked.Read(ref done);
                    var nextEmitAt = baseSent;

                    // 片内进度：每读满 256KB 上报一次，避免逐块刷新把 UI 压垮
                    void OnChunkRead(long read)
                    {
                        var now = baseSent + read;
                        if (now < nextEmitAt) return;
                        nextEmitAt = now + 256 * 1024;
                        Emit(now, "Uploading");
                    }

                    var (ok, code, msg) = await UploadChunkAsync(endpoints, localPath, start, length, fileName,
                        fi.Length, totalChunks, part, directoryId, uniqueId, fileInfoEnc,
                        psToken ?? _session.PsToken, GuessMimeType(fileName), OnChunkRead,
                        abort.Token).ConfigureAwait(false);

                    if (!ok)
                    {
                        // 1001 = 登录态失效（在别处重新登录会顶掉当前 token）
                        if (code == "1001")
                            throw new WoCloudException(code, "登录已失效，请重新登录后再上传");
                        throw new WoCloudException(code ?? "UPLOAD_FAIL",
                            $"服务端拒绝（{code ?? "?"}）：{msg ?? $"分片 {part}/{totalChunks} 上传失败"}");
                    }

                    Emit(Interlocked.Add(ref done, length), "Uploading");
                }
            }
            catch (Exception ex)
            {
                lock (errorLock)
                {
                    // 取消类异常通常是「别的分片先失败」的连带结果，优先保留真实错误
                    if (firstError is null ||
                        (firstError is OperationCanceledException && ex is not OperationCanceledException))
                        firstError = ex;
                }
                abort.Cancel();   // 任一分片失败，其余立即停止，避免继续白传
            }
        }

        var workers = Enumerable.Range(0, Math.Clamp(Concurrency, 1, Math.Max(1, totalChunks)))
            .Select(_ => Task.Run(WorkerAsync))
            .ToArray();
        await Task.WhenAll(workers).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        if (firstError is not null) throw firstError;
        Emit(fi.Length, "Completed");
    }

    /// <summary>批量上传（内部顺序执行；每文件内部并发 3 分片）。</summary>
    public async Task UploadBatchAsync(IEnumerable<(string LocalPath, string DirectoryId)> items,
        IProgress<(string File, double Progress)>? progress = null, CancellationToken ct = default)
    {
        foreach (var (path, dir) in items)
        {
            var name = Path.GetFileName(path);
            var inner = new Progress<double>(p => progress?.Report((name, p)));
            await UploadAsync(path, dir, progress: inner, ct: ct).ConfigureAwait(false);
        }
    }

    private (long Start, int Length) GetChunkRange(long size, int part) =>
        ((part - 1) * (long)ChunkSizeBytes,
         (int)Math.Min(ChunkSizeBytes, size - (part - 1) * (long)ChunkSizeBytes));

    private async Task<(bool Ok, string? Code, string? Msg)> UploadChunkAsync(
        IReadOnlyList<string> endpoints,
        string localPath, long start, int partSize, string fileName, long fileSize, int totalPart, int partIndex,
        string directoryId, string uniqueId, string fileInfoEnc, string? psToken, string mime,
        Action<long>? onRead, CancellationToken ct)
    {
        string? lastCode = null, lastMsg = null;

        // 逐节点尝试：首选节点（手机端网关）给 1 次重试，失败切下一个节点。
        // 分片无状态、两端存储后端相同，跨节点续传没有副作用。
        for (var e = 0; e < endpoints.Count; e++)
        {
            var endpoint = endpoints[e];
            var attempts = e == 0 ? MaxRetries + 1 : 1;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
            try
            {
                using var form = new MultipartFormDataContent();

                // 字段顺序必须与浏览器一致：文本字段在前，file 分片最后
                form.Add(new StringContent(uniqueId), "uniqueId");
                form.Add(new StringContent(_session.AccessToken!), "accessToken");
                form.Add(new StringContent(fileName), "fileName");
                // 浏览器无论有没有都发 psToken（无值时为字符串 "undefined"），缺失会被判参数错误
                form.Add(new StringContent(string.IsNullOrEmpty(psToken) ? "undefined" : psToken), "psToken");
                form.Add(new StringContent(fileSize.ToString()), "fileSize");
                form.Add(new StringContent(totalPart.ToString()), "totalPart");
                form.Add(new StringContent(partSize.ToString()), "partSize");
                form.Add(new StringContent(partIndex.ToString()), "partIndex");
                form.Add(new StringContent("wocloud"), "channel");
                form.Add(new StringContent(directoryId), "directoryId");
                form.Add(new StringContent(fileInfoEnc), "fileInfo");

                // 必须用 ChunkStream 包一层：直接给 StreamContent 一个定位过的 FileStream，
                // 内容长度会算成「文件剩余全部字节」，分片就成了整个文件（大视频卡死的根因）
                var chunk = new ChunkStream(localPath, start, partSize, 81920, onRead);
                var fileContent = new StreamContent(chunk, 81920);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(mime);
                form.Add(fileContent, "file", fileName);

                using var resp = await _http.PostAsync(endpoint, form, ct).ConfigureAwait(false);
                var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                // 服务端即使参数错误也返回 HTTP 200，必须解析业务码
                var (code, msg) = ParseUploadResult(text);
                if (resp.IsSuccessStatusCode && code == "0000") return (true, code, msg);
                lastCode = code ?? ((int)resp.StatusCode).ToString();
                lastMsg = msg;
                // 4xx/500 属于硬错误，重试没意义，换下一个节点
                if ((int)resp.StatusCode is >= 400 and < 500 or >= 500) break;
            }
            catch (Exception) when (attempt < attempts - 1 && !ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
            }
        }
        return (false, lastCode, lastMsg);
    }

    /// <summary>解析 upload2C 返回 {code,msg}（成功为 "0000"）。</summary>
    private static (string? Code, string? Msg) ParseUploadResult(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            var root = doc.RootElement;
            // 兼容 {code} 与 {meta:{code}} 两种外壳
            foreach (var el in new[] { root, root.TryGetProperty("meta", out var m) ? m : root })
            {
                var code = el.TryGetProperty("code", out var c) ? c.ToString() : null;
                if (code is null) continue;
                var msg = el.TryGetProperty("msg", out var g) ? g.ToString()
                    : el.TryGetProperty("message", out var g2) ? g2.ToString() : null;
                return (code, msg);
            }
            return (null, text.Length > 200 ? text[..200] : text);
        }
        catch
        {
            return (null, text.Length > 200 ? text[..200] : text);
        }
    }

    /// <summary>前端同款唯一键：时间戳_6 位随机串（用作断点续传键，不是文件名）。</summary>
    private static string ComputeUniqueId()
    {
        var rnd = Random.Shared.Next(100000, 999999);
        return $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{rnd}";
    }

    private const string Base62 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>批次号 batchNo（32 位 base62，fileInfo 里的必填字段，缺失返回 1000）。</summary>
    private static string ComputeBatchNo()
    {
        Span<char> buf = stackalloc char[32];
        for (var i = 0; i < buf.Length; i++) buf[i] = Base62[Random.Shared.Next(Base62.Length)];
        return new string(buf);
    }

    /// <summary>按扩展名给出 MIME（浏览器上传时 file 分片用的就是真实类型）。</summary>
    private static string GuessMimeType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        ".mp4" or ".m4v" => "video/mp4",
        ".mov" => "video/quicktime",
        ".mkv" => "video/x-matroska",
        ".avi" => "video/x-msvideo",
        ".mp3" => "audio/mpeg",
        ".flac" => "audio/flac",
        ".wav" => "audio/wav",
        ".m4a" or ".aac" => "audio/aac",
        ".pdf" => "application/pdf",
        ".txt" or ".md" or ".log" or ".csv" or ".cs" or ".js" or ".ts" or ".json" or ".xml"
            or ".html" or ".css" or ".yml" or ".yaml" => "text/plain",
        ".doc" or ".docx" => "application/msword",
        ".xls" or ".xlsx" => "application/vnd.ms-excel",
        ".ppt" or ".pptx" => "application/vnd.ms-powerpoint",
        ".zip" => "application/zip",
        ".rar" => "application/x-rar-compressed",
        ".7z" => "application/x-7z-compressed",
        _ => "application/octet-stream",
    };

    /// <summary>按扩展名推断 fileType：1图片 2视频 3音频 4文档 5其他。</summary>
    private static string GuessFileType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".heic" => "1",
            ".mp4" or ".mkv" or ".mov" or ".avi" or ".wmv" or ".webm" or ".flv" => "2",
            ".mp3" or ".flac" or ".wav" or ".aac" or ".m4a" or ".ogg" or ".wma" => "3",
            ".txt" or ".md" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx"
                or ".pdf" or ".csv" or ".json" or ".xml" or ".zip" or ".rar" or ".7z" => "4",
            _ => "5",
        };
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}

/// <summary>upload2C 响应（宽松解析）。</summary>
internal sealed class UploadChunkResponse
{
    [JsonPropertyName("meta")] public UploadMeta? Meta { get; set; }
}

internal sealed class UploadMeta
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}
