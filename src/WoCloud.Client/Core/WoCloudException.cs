namespace WoCloud.Client.Core;

/// <summary>联通云盘业务异常（携带 RSP_CODE / RSP_DESC）。</summary>
public class WoCloudException : Exception
{
    public string Code { get; }
    public string? LogId { get; }

    public WoCloudException(string code, string message, string? logId = null)
        : base($"[{code}] {message}")
    {
        Code = code;
        LogId = logId;
    }

    /// <summary>是否为会话失效（应引导重新登录）。</summary>
    public bool IsSessionExpired => Code is "130007" or "130008" or "1001";
}
