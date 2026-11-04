using System.Text.Json.Serialization;
using WoCloud.Client.Core;

namespace WoCloud.Client.Space;

/// <summary>
/// 私密空间（解锁、设密、移入移出）：
/// PrivateSpaceLogin / SetPwd / ReSetPwd / MoveInOrOutPrivateSpace。
/// 空间标识 spaceType = "4"，浏览时需带解锁得到的 <c>psToken</c>。
/// </summary>
public sealed class PrivateSpaceApi
{
    private readonly DispatcherClient _dispatcher;
    private readonly SessionState _session;

    public PrivateSpaceApi(DispatcherClient dispatcher, SessionState session)
    {
        _dispatcher = dispatcher;
        _session = session;
    }

    private string ClientId => _session.ClientId;

    /// <summary>是否已解锁（本地持有 psToken）。</summary>
    public bool IsUnlocked => !string.IsNullOrEmpty(_session.PsToken);

    /// <summary>解锁（实测：{clientId, pwd} → {isPass:"1"/"0", psToken, desc}）。</summary>
    public async Task<PrivateSpaceLoginResult?> LoginAsync(string password, CancellationToken ct = default)
    {
        var r = await _dispatcher.CallAsync<PrivateSpaceLoginResult>("PrivateSpaceLogin", new
        {
            clientId = ClientId,
            pwd = password,
        }, ct: ct).ConfigureAwait(false);
        if (r is { IsPass: "1", PsToken.Length: > 0 })
            _session.SetPsToken(r.PsToken!);
        return r;
    }

    /// <summary>首次设置密码（返回 psToken 并自动保存）。</summary>
    public async Task<PrivateSpaceLoginResult?> SetPasswordAsync(string password, CancellationToken ct = default)
    {
        var r = await _dispatcher.CallAsync<PrivateSpaceLoginResult>("SetPwd", new
        {
            clientId = ClientId,
            pwd = password,
        }, ct: ct).ConfigureAwait(false);
        if (r?.PsToken is { Length: > 0 } t) _session.SetPsToken(t);
        return r;
    }

    /// <summary>重置密码（短信验证码流程，sendSms 后用此接口）。</summary>
    public Task<object?> ResetPasswordAsync(string password, string smsCode, CancellationToken ct = default) =>
        _dispatcher.CallAsync<object>("ReSetPwd", new
        {
            clientId = ClientId,
            pwd = password,
            smsCode,
        }, ct: ct);

    /// <summary>移入私密空间（moveType="0"）。</summary>
    public Task<object?> MoveInAsync(IEnumerable<string>? fileIds, IEnumerable<string>? dirIds,
        string targetDirId = "0", CancellationToken ct = default) =>
        MoveAsync("0", targetDirId, fileIds, dirIds, ct);

    /// <summary>移出到个人云（moveType="1"）。</summary>
    public Task<object?> MoveOutAsync(IEnumerable<string>? fileIds, IEnumerable<string>? dirIds,
        string targetDirId = "0", CancellationToken ct = default) =>
        MoveAsync("1", targetDirId, fileIds, dirIds, ct);

    /// <summary>移入/移出（实测：{clientId, targetDirId, moveType, dirIds, fileIds}）。</summary>
    private Task<object?> MoveAsync(string moveType, string targetDirId,
        IEnumerable<string>? fileIds, IEnumerable<string>? dirIds, CancellationToken ct) =>
        _dispatcher.CallAsync<object>("MoveInOrOutPrivateSpace", new
        {
            clientId = ClientId,
            targetDirId,
            moveType,
            dirIds = dirIds?.ToList() ?? [],
            fileIds = fileIds?.ToList() ?? [],
        }, ct: ct);
}

/// <summary>私密空间解锁/设密响应。</summary>
public sealed class PrivateSpaceLoginResult
{
    /// <summary>"1"=通过，"0"=密码错误。</summary>
    [JsonPropertyName("isPass")] public string? IsPass { get; set; }
    [JsonPropertyName("psToken")] public string? PsToken { get; set; }
    [JsonPropertyName("desc")] public string? Desc { get; set; }
}
