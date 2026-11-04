using System.Net.Http;
using WoCloud.Client.Transfer;
using Xunit;

namespace WoCloud.Client.Tests;

/// <summary>
/// 分片流回归测试。
/// 背景：直接给 StreamContent 传「定位到偏移的 FileStream」，内容长度会被算成文件剩余全部字节，
/// 导致 8MB 分片实际上传整个文件——大视频上传表现为进度停滞。
/// </summary>
public class ChunkStreamTests : IDisposable
{
    // 沙箱环境禁写用户目录，测试文件跟随输出目录（与 SessionStateTests 一致）
    private static readonly string TestDir = Path.Combine(AppContext.BaseDirectory, "test-data");
    private readonly string _path = Path.Combine(TestDir, $"chunk-{Guid.NewGuid():N}.bin");
    private const int FileSize = 20 * 1024 * 1024;
    private const int ChunkSize = 8 * 1024 * 1024;

    public ChunkStreamTests()
    {
        Directory.CreateDirectory(TestDir);
        // 写入可校验的字节模式，不占实际磁盘空间之外的内存
        using var fs = new FileStream(_path, FileMode.Create, FileAccess.Write);
        fs.SetLength(FileSize);
        var pattern = new byte[256 * 1024];
        for (int i = 0; i < pattern.Length; i++) pattern[i] = (byte)(i % 251);
        for (long offset = 0; offset < FileSize; offset += pattern.Length)
            fs.Write(pattern, 0, (int)Math.Min(pattern.Length, FileSize - offset));
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Length_IsChunkSize_NotRemainingFile()
    {
        using var chunk = new ChunkStream(_path, 0, ChunkSize, 81920);
        Assert.Equal(ChunkSize, chunk.Length);
        Assert.Equal(0, chunk.Position);
    }

    [Fact]
    public async Task Read_StopsAtChunkBoundary()
    {
        var start = (long)ChunkSize;                 // 第二片
        using var chunk = new ChunkStream(_path, start, ChunkSize, 81920);
        var buffer = new byte[1024 * 1024];
        long total = 0;

        var first = await chunk.ReadAsync(buffer, 0, buffer.Length);
        total += first;
        var firstByte = buffer[0];

        int n;
        while ((n = await chunk.ReadAsync(buffer, 0, buffer.Length)) > 0)
            total += n;

        Assert.Equal(ChunkSize, total);
        Assert.Equal(ChunkSize, chunk.Position);

        // 分片内容必须从 start 开始，而不是文件开头
        using var raw = new FileStream(_path, FileMode.Open, FileAccess.Read);
        raw.Seek(start, SeekOrigin.Begin);
        Assert.Equal((byte)raw.ReadByte(), firstByte);
    }

    [Fact]
    public void ReadsProgressively_ReportsReadBytes()
    {
        var last = 0L;
        using var chunk = new ChunkStream(_path, 0, ChunkSize, 81920, read => last = read);
        var buffer = new byte[81920];
        while (chunk.Read(buffer, 0, buffer.Length) > 0) { }

        Assert.Equal(ChunkSize, last);
    }

    [Fact]
    public void MultipartContentLength_MatchesChunkSize()
    {
        using var chunk = new ChunkStream(_path, ChunkSize, ChunkSize, 81920);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("unique-1"), "uniqueId");
        form.Add(new StreamContent(chunk, 81920), "file", "v.mp4");

        var length = form.Headers.ContentLength;
        Assert.NotNull(length);
        // 文本字段 + 分隔符开销很小；关键是不能退化成「文件剩余大小」
        Assert.InRange(length!.Value, ChunkSize, ChunkSize + 4096);
    }
}
