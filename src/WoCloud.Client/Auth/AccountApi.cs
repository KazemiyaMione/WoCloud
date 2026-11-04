using System.Net.Http.Headers;
using System.Text.Json;
using WoCloud.Client.Core;

namespace WoCloud.Client.Auth;

/// <summary>
/// 账号设置相关接口（个人资料 / 头像 / 修改密码）。
/// 参数结构取自网页端源码（WebUpdateUserInfo、AppUpdateUserV2 type=5、/api-user/update/user/head）。
/// </summary>
public sealed class AccountApi
{
    private readonly DispatcherClient _dispatcher;
    private readonly SessionState _session;
    private readonly HttpClient _http;

    public AccountApi(DispatcherClient dispatcher, SessionState session, HttpClient? httpClient = null)
    {
        _dispatcher = dispatcher;
        _session = session;
        _http = httpClient ?? new HttpClient();
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://pan.wo.cn");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://pan.wo.cn/");
    }

    /// <summary>修改个人资料（昵称 / 性别 / 生日 / 头像地址）。</summary>
    /// <param name="sex">"1"=男 "2"=女（服务端要求非空）</param>
    /// <param name="birthdayMs">生日毫秒时间戳</param>
    public Task UpdateProfileAsync(string userName, string sex, long birthdayMs, string? headUrl,
        CancellationToken ct = default) =>
        _dispatcher.CallAsync<JsonElement?>("WebUpdateUserInfo", new
        {
            accessToken = _session.AccessToken,
            userName,
            sex,
            birthday = birthdayMs,
            headUrl = headUrl ?? "",
        }, WoCloudConstants.ChannelApiUser, SecretType.ClientSecret, ct: ct);

    /// <summary>修改登录密码：短信验证码 + 新密码（服务端 type=5 分支，无需旧密码）。</summary>
    public Task ChangePasswordAsync(string phone, string smsCode, string newPassword,
        CancellationToken ct = default) =>
        _dispatcher.CallAsync<JsonElement?>("AppUpdateUserV2", new
        {
            type = "5",
            phone,
            smsCode,
            password = newPassword,
            accessToken = _session.AccessToken,
        }, WoCloudConstants.ChannelApiUser, SecretType.ClientSecret, ct: ct);

    /// <summary>上传头像（原图直传，服务端返回新的 headUrl）。</summary>
    public async Task<string> UploadAvatarAsync(byte[] image, string fileName, string contentType,
        CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);

        using var msg = new HttpRequestMessage(HttpMethod.Post, WoCloudConstants.UpdateHeadUrl) { Content = content };
        msg.Headers.TryAddWithoutValidation("accesstoken", _session.AccessToken);

        using var resp = await _http.SendAsync(msg, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        JsonElement rsp;
        try
        {
            using var doc = JsonDocument.Parse(text);
            rsp = doc.RootElement.TryGetProperty("RSP", out var r) ? r.Clone() : default;
        }
        catch (JsonException)
        {
            throw new WoCloudException("AVATAR_BAD_RESPONSE", $"头像上传返回异常：{text[..Math.Min(120, text.Length)]}");
        }

        var code = rsp.ValueKind == JsonValueKind.Object && rsp.TryGetProperty("RSP_CODE", out var c)
            ? c.GetString() : null;
        if (code != "0000")
        {
            var desc = rsp.ValueKind == JsonValueKind.Object && rsp.TryGetProperty("RSP_DESC", out var m)
                ? m.GetString() : null;
            throw new WoCloudException(code ?? "AVATAR_FAIL", desc ?? "头像上传失败");
        }

        return rsp.TryGetProperty("DATA", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString() ?? ""
            : "";
    }
}
