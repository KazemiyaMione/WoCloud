using System.Text.Json;

namespace WoCloud.Client.Core;

/// <summary>登录态快照（持久化到会话存储目录 session.json）。</summary>
public sealed class SessionSnapshot
{
    public string? AccessToken { get; set; }
    public string ClientId { get; set; } = WoCloudConstants.DefaultClientId;
    public string? PsToken { get; set; }
    public string? Phone { get; set; }
    public DateTime? SavedAtUtc { get; set; }
}

/// <summary>
/// 会话状态容器：token / clientId / psToken，负责持久化与失效事件。
/// 存储位置：LocalAppData\WoCloud（不可写时自动回退 Roaming → Temp → 程序目录\data，
/// 可通过 <see cref="OverrideStoreDirectory"/> 显式指定，如便携模式）。
/// </summary>
public sealed class SessionState
{
    private const string StoreFileName = "session.json";

    private readonly object _lock = new();

    /// <summary>显式指定存储目录（测试/便携模式）。置空则自动解析。</summary>
    public static string? OverrideStoreDirectory { get; set; }

    private string? _resolvedDir;

    /// <summary>会话失效时触发（RSP_CODE 130007/130008/1001），UI 应切换到登录页。</summary>
    public event Action? SessionExpired;

    public string? AccessToken { get; private set; }
    public string ClientId { get; private set; } = WoCloudConstants.DefaultClientId;
    public string? PsToken { get; private set; }
    public string? Phone { get; private set; }

    public bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken);

    public string ClientSecret =>
        WoCloudConstants.ClientSecrets.TryGetValue(ClientId, out var s)
            ? s
            : throw new InvalidOperationException($"未知 clientId: {ClientId}，无对应 secretKey");

    public void SetLogin(string accessToken, string? phone = null, string? psToken = null)
    {
        AccessToken = accessToken;
        Phone = phone ?? Phone;
        PsToken = psToken ?? PsToken;
        Save();
    }

    public void SetToken(string accessToken) => SetLogin(accessToken);

    public void SetPsToken(string psToken)
    {
        PsToken = psToken;
        Save();
    }

    public void SetClientId(string clientId)
    {
        ClientId = clientId;
        Save();
    }

    /// <summary>清除登录态（登出或失效）。</summary>
    public void Clear()
    {
        AccessToken = null;
        PsToken = null;
        Save();
    }

    /// <summary>标记会话失效：清空本地 token 并持久化，避免下次启动继续用过期 token。</summary>
    public void NotifySessionExpired()
    {
        AccessToken = null;
        PsToken = null;
        try { Save(); } catch { /* 存储不可写时忽略 */ }
        SessionExpired?.Invoke();
    }

    private string ResolveStoreDirectory()
    {
        if (_resolvedDir is not null) return _resolvedDir;

        var candidates = new List<string?>(4);
        if (OverrideStoreDirectory is not null) candidates.Add(OverrideStoreDirectory);
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WoCloud"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WoCloud"));
        candidates.Add(Path.Combine(Path.GetTempPath(), "WoCloud"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "data"));

        Exception? last = null;
        foreach (var dir in candidates)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            try
            {
                Directory.CreateDirectory(dir);
                // 探针：确认可写（部分沙箱允许建目录但拒绝写文件）
                var probe = Path.Combine(dir, ".probe");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                _resolvedDir = dir;
                return dir;
            }
            catch (Exception e)
            {
                last = e;
            }
        }
        throw new InvalidOperationException("没有可用的会话存储目录", last);
    }

    private string StorePath => Path.Combine(ResolveStoreDirectory(), StoreFileName);

    public void Save()
    {
        lock (_lock)
        {
            var snap = new SessionSnapshot
            {
                AccessToken = AccessToken,
                ClientId = ClientId,
                PsToken = PsToken,
                Phone = Phone,
                SavedAtUtc = DateTime.UtcNow,
            };
            File.WriteAllText(StorePath, JsonSerializer.Serialize(snap, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
        }
    }

    /// <summary>从磁盘恢复会话；返回是否恢复了有效登录态。</summary>
    public bool TryRestore()
    {
        try
        {
            if (!File.Exists(StorePath)) return false;
            var snap = JsonSerializer.Deserialize<SessionSnapshot>(File.ReadAllText(StorePath));
            if (snap is null) return false;
            AccessToken = snap.AccessToken;
            ClientId = snap.ClientId;
            PsToken = snap.PsToken;
            Phone = snap.Phone;
            return IsLoggedIn;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>删除持久化文件（完全登出）。</summary>
    public void DeleteStore()
    {
        try { if (File.Exists(StorePath)) File.Delete(StorePath); } catch { /* ignore */ }
    }
}
