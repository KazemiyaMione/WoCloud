namespace WoCloud.Client.Transfer;

/// <summary>
/// 只暴露文件某一段的只读流。
/// <para>
/// 必须用它代替「FileStream 定位到偏移 + StreamContent」：<see cref="System.Net.Http.StreamContent"/>
/// 对可寻址流取 <c>Length - Position</c> 作为内容长度，直接用 FileStream 会把
/// 「文件剩余全部字节」当成一个分片发出去（20MB 文件的 8MB 分片实际发 20MB），
/// 大文件因此表现为进度不动、上传极慢。
/// </para>
/// </summary>
internal sealed class ChunkStream : Stream
{
    private readonly Stream _inner;
    private readonly long _start;
    private readonly long _length;
    private readonly Action<long>? _onRead;
    private long _position;

    /// <param name="path">本地文件</param>
    /// <param name="start">分片起始偏移</param>
    /// <param name="length">分片长度</param>
    /// <param name="bufferSize">缓冲区大小</param>
    /// <param name="onRead">已读字节回调（用于片内进度）</param>
    public ChunkStream(string path, long start, long length, int bufferSize, Action<long>? onRead = null)
    {
        _inner = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            Math.Max(4096, bufferSize), useAsync: true);
        _start = start;
        _length = length;
        _onRead = onRead;
        _inner.Seek(start, SeekOrigin.Begin);
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;

    /// <summary>分片长度（HttpClient 用它计算 Content-Length）。</summary>
    public override long Length => _length;

    /// <summary>分片内已读位置（相对分片起点）。</summary>
    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var n = _inner.Read(buffer[..(int)Math.Min(buffer.Length, Remaining())]);
        Advance(n);
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var want = (int)Math.Min(buffer.Length, Remaining());
        if (want <= 0) return 0;
        var n = await _inner.ReadAsync(buffer[..want], ct).ConfigureAwait(false);
        Advance(n);
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => _length + offset,
        };
        _position = Math.Clamp(target, 0, _length);
        _inner.Seek(_start + _position, SeekOrigin.Begin);
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private long Remaining() => _length - _position;

    private void Advance(int n)
    {
        if (n <= 0) return;
        _position += n;
        _onRead?.Invoke(_position);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
