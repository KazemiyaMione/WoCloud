using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WoCloud.Client.Core;
using WoCloud.Client.Models;

namespace WoCloud.Client.Auth;

/// <summary>
/// 登录/登出 API：
/// 1. 短信验证码登录（AppLoginByMobile）—— 一段式
/// 2. 账号密码登录（PcWebLogin 6008 → sendMessageCodeBase → PcLoginVerifyCode）—— 两段式
/// 3. 扫码登录（QRCode/generate → query 轮询）
/// 4. Token 粘贴直通
/// </summary>
public sealed class AuthApi
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly DispatcherClient _dispatcher;
    private readonly HttpClient _http;
    private readonly SessionState _session;

    public AuthApi(DispatcherClient dispatcher, SessionState session, HttpClient? httpClient = null)
    {
        _dispatcher = dispatcher;
        _session = session;
        _http = httpClient ?? new HttpClient();
        // 服务端（BFF/开放接口）会校验 Origin/Referer/UA，需模拟浏览器指纹
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://pan.wo.cn");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://pan.wo.cn/");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
    }

    // ---------- 图形验证码 ----------

    /// <summary>生成图形验证码对（uuid + 图片 URL）。风控触发时使用。</summary>
    public (string Uuid, string ImageUrl) NewCaptcha()
    {
        var uuid = Guid.NewGuid().ToString("D"); // RFC4122 v4
        return (uuid, WoCloudConstants.GetVerifyCodeUrl(uuid));
    }

    /// <summary>查询指定账号是否需要图形验证码（PcWebLoginVerifyCodeShow）。</summary>
    public Task<bool> IsCaptchaRequiredAsync(string phone, CancellationToken ct = default)
    {
        return CallApiUserAsync<VerifyCodeShowData>("PcWebLoginVerifyCodeShow",
            new { account = phone, clientSecret = _session.ClientSecret }, ct)
            .ContinueWith(t => t.Result?.IsNeedVerifyCode == 1, ct);
    }

    /// <summary>发送登录短信验证码（sendMessageCodeBase；captcha 可选，风控时必填）。</summary>
    public async Task<bool> SendSmsCodeAsync(string phone, (string Uuid, string Code)? captcha = null, CancellationToken ct = default)
    {
        var inner = captcha is { } c
            ? new { operateType = "1", phone, uuid = c.Uuid, verifyCode = c.Code }
            : (object)new { operateType = "1", phone };
        var body = new
        {
            func = "pc_send",
            clientId = _session.ClientId,
            param = CryptoHelper.Encrypt(JsonSerializer.Serialize(inner, JsonOpts), _session.ClientSecret),
        };
        var (ok, code, message, _) = await PostRawJsonAsync(WoCloudConstants.SendMessageCodeUrl, body, ct).ConfigureAwait(false);
        if (ok) return true;
        throw new WoCloudException(code ?? "SMS_ERR", message ?? "短信发送失败");
    }

    // ---------- 登录通道 ----------

    /// <summary>短信验证码直接登录（AppLoginByMobile）。</summary>
    public async Task<string> LoginBySmsAsync(string phone, string smsCode, CancellationToken ct = default)
    {
        var resp = await _dispatcher.SendRawAsync("AppLoginByMobile",
            new { phone, smsCode, clientSecret = _session.ClientSecret },
            WoCloudConstants.ChannelApiUser, SecretType.ClientSecret, ct: ct).ConfigureAwait(false);
        var (code, accessToken) = ExtractToken(resp);
        if (code == "0000" && accessToken is not null)
        {
            _session.SetLogin(accessToken, phone);
            return accessToken;
        }
        throw new WoCloudException(code, Describe(resp));
    }

    /// <summary>账号密码登录第一步（PcWebLogin）。返回 true=一步成功（罕见）；抛 6008=需短信二次验证。</summary>
    public async Task<bool> LoginByPasswordAsync(string phone, string password,
        (string Uuid, string Code)? captcha = null, CancellationToken ct = default)
    {
        object param = captcha is { } c
            ? new { phone, password, uuid = c.Uuid, verifyCode = c.Code, clientSecret = _session.ClientSecret }
            : new { phone, password, clientSecret = _session.ClientSecret };
        var resp = await _dispatcher.SendRawAsync("PcWebLogin", param,
            WoCloudConstants.ChannelApiUser, SecretType.ClientSecret, ct: ct).ConfigureAwait(false);
        var code = resp.Rsp?.Code ?? "NO_RSP";
        if (code == "0000")
        {
            // 少数情况密码登录直接完成：尝试取 token
            var (_, token) = ExtractToken(resp);
            if (token is not null) { _session.SetLogin(token, phone); return true; }
            throw new WoCloudException("0000", "登录成功但未返回 token，请走短信二次验证");
        }
        if (code == "6008")
        {
            // 密码校验通过，需短信二次验证
            await SendSmsCodeAsync(phone, captcha, ct).ConfigureAwait(false);
            throw new NeedSmsVerifyException("6008", "密码校验通过，已发送短信验证码，请调用 CompleteSmsVerifyAsync");
        }
        throw new WoCloudException(code, Describe(resp));
    }

    /// <summary>账号密码登录第二步（PcLoginVerifyCode）。需先 LoginByPasswordAsync 抛 6008 后收到短信。</summary>
    public async Task<string> CompleteSmsVerifyAsync(string phone, string smsCode,
        (string Uuid, string Code)? captcha = null, CancellationToken ct = default)
    {
        object param = captcha is { } c
            ? new { phone, messageCode = smsCode, uuid = c.Uuid, verifyCode = c.Code, clientSecret = _session.ClientSecret }
            : new { phone, messageCode = smsCode, clientSecret = _session.ClientSecret };
        var resp = await _dispatcher.SendRawAsync("PcLoginVerifyCode", param,
            WoCloudConstants.ChannelApiUser, SecretType.ClientSecret, ct: ct).ConfigureAwait(false);
        var (code, token) = ExtractToken(resp);
        if (code == "0000" && token is not null)
        {
            _session.SetLogin(token, phone);
            return token;
        }
        throw new WoCloudException(code, Describe(resp));
    }

    // ---------- 扫码登录 ----------

    /// <summary>生成登录二维码。返回二维码 base64（png）与轮询用票据。（GET，实测 200）</summary>
    public async Task<QrCodeTicket> GenerateQrCodeAsync(CancellationToken ct = default)
    {
        var (ok, code, data) = await GetRawJsonAsync(WoCloudConstants.QrCodeGenerateUrl, null, ct).ConfigureAwait(false);
        if (!ok || data is null)
            throw new WoCloudException(code ?? "QR_ERR", "二维码生成失败");
        var q = data.Value.Deserialize<QrCodeData>(JsonOpts);
        if (q?.Image is null || q.Uuid is null)
            throw new WoCloudException("QR_FMT", "二维码响应格式异常: " + data.Value.GetRawText());
        return new QrCodeTicket(q.Uuid, "data:image/png;base64," + q.Image);
    }

    /// <summary>轮询扫码状态（GET，实测 200；间隔 2 秒）。state==Confirmed 时已自动写入会话 token。</summary>
    public async Task<QrPollState> PollQrCodeAsync(string ticketUuid, CancellationToken ct = default)
    {
        var (ok, code, data) = await GetRawJsonAsync(WoCloudConstants.QrCodeQueryUrl,
            new Dictionary<string, string?> { ["uuid"] = ticketUuid }, ct).ConfigureAwait(false);
        if (!ok || data is null)
            throw new WoCloudException(code ?? "QR_ERR", "二维码状态查询失败");
        var q = data.Value.Deserialize<QrCodeData>(JsonOpts);
        if (q?.State is null) return QrPollState.Waiting;
        switch (q.State)
        {
            case 3:
                if (string.IsNullOrEmpty(q.Token))
                    throw new WoCloudException("QR_TOKEN", "扫码确认但响应缺少 token");
                _session.SetLogin(q.Token);
                return QrPollState.Confirmed;
            case 2: return QrPollState.Scanned;
            default: return QrPollState.Waiting;
        }
    }

    // ---------- 其他 ----------

    /// <summary>Token 粘贴直通（开发调试）。</summary>
    public void SetTokenDirect(string token, string? phone = null) => _session.SetLogin(token, phone);

    /// <summary>
    /// 联通手机营业厅 App Token 兑换云盘登录态（手机端免登链路，参数取自青龙脚本逆向）：
    /// 1. GET m.client.10010.com/edop_ng/getTicketByNative?appId=edop_unicom_d67b3e30&token={ecs_token} → ticket
    /// 2. wohome dispatcher「HandheldHallAutoLoginV2」，body 明文 {clientId:"1001000003", ticket} → RSP.DATA.token
    /// 兑换出的 accessToken 即手机端 AES 密钥（前 16 位）。
    /// </summary>
    public async Task<string> LoginByUnicomAppTokenAsync(string ecsToken, CancellationToken ct = default)
    {
        // 第一步：App ecs_token 换 ticket（需带联通 App 的 UA，否则可能被风控拦截）
        using var ticketMsg = new HttpRequestMessage(HttpMethod.Get,
            $"{WoCloudConstants.GetTicketByNativeUrl}?appId=edop_unicom_d67b3e30&token={Uri.EscapeDataString(ecsToken)}");
        ticketMsg.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (iPhone; CPU iPhone OS 16_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148 unicom{version:iphone_c@12.0301}");

        using var ticketResp = await _http.SendAsync(ticketMsg, ct).ConfigureAwait(false);
        var ticketText = await ticketResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        string ticket;
        try
        {
            using var doc = JsonDocument.Parse(ticketText);
            ticket = doc.RootElement.TryGetProperty("ticket", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? "" : "";
        }
        catch (JsonException)
        {
            throw new WoCloudException("TICKET_BAD_JSON", $"票据接口返回异常：{ticketText[..Math.Min(120, ticketText.Length)]}");
        }

        if (ticket.Length == 0)
            throw new WoCloudException("TICKET_FAIL", "兑换票据失败：ecs_token 无效或已过期");

        // 第二步：ticket 换云盘 accessToken（明文 body，UA 模拟安卓 App）
        var resp = await _dispatcher.SendRawAsync("HandheldHallAutoLoginV2",
            new { clientId = "1001000003", ticket },
            WoCloudConstants.ChannelWohome, SecretType.Token, encrypt: false, ct: ct).ConfigureAwait(false);

        var code = resp.Rsp?.Code ?? "NO_RSP";
        var token = resp.Rsp?.Data is { } d && d.ValueKind == JsonValueKind.Object &&
                    d.TryGetProperty("token", out var tk) && tk.ValueKind == JsonValueKind.String
            ? tk.GetString() : null;

        if (code != "0000" || string.IsNullOrEmpty(token))
            throw new WoCloudException(code, resp.Rsp?.Desc ?? "App Token 兑换云盘登录态失败");

        _session.SetLogin(token!);
        return token!;
    }

    /// <summary>退出登录。</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (_session.IsLoggedIn)
        {
            try
            {
                await _dispatcher.CallAsync<object>("AppLogout", null,
                    WoCloudConstants.ChannelApiUser, SecretType.ClientSecret, ct: ct).ConfigureAwait(false);
            }
            catch { /* 登出失败不阻塞本地清理 */ }
        }
        _session.Clear();
    }

    /// <summary>查询用户信息（AppQueryUser）。</summary>
    public Task<CloudUserInfo?> GetUserInfoAsync(CancellationToken ct = default) =>
        _dispatcher.CallAsync<CloudUserInfo>("AppQueryUser",
            new { accessToken = _session.AccessToken },
            WoCloudConstants.ChannelApiUser, SecretType.ClientSecret, ct: ct);

    // ---------- 内部工具 ----------

    private Task<VerifyCodeShowData?> CallApiUserAsync<T>(string key, object param, CancellationToken ct) =>
        _dispatcher.CallAsync<VerifyCodeShowData>(key, param,
            WoCloudConstants.ChannelApiUser, SecretType.ClientSecret, ct: ct);

    /// <summary>裸 GET（QRCode generate/query），带 X-YP 头；query 可空或 kv 参数。</summary>
    private async Task<(bool Ok, string? Code, JsonElement? Data)> GetRawJsonAsync(
        string url, Dictionary<string, string?>? query, CancellationToken ct)
    {
        var full = url;
        if (query is { Count: > 0 })
        {
            var qp = string.Join("&", query
                .Where(kv => kv.Value is not null)
                .Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value!)));
            full = url + "?" + qp;
        }
        using var msg = new HttpRequestMessage(HttpMethod.Get, full);
        msg.Headers.TryAddWithoutValidation("X-YP-Access-Token", _session.AccessToken ?? "");
        msg.Headers.TryAddWithoutValidation("access-token", _session.AccessToken ?? "");
        msg.Headers.TryAddWithoutValidation("X-YP-Client-Id", _session.ClientId);
        msg.Headers.TryAddWithoutValidation("client-id", _session.ClientId);
        using var resp = await _http.SendAsync(msg, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var metaCode = root.TryGetProperty("meta", out var meta) &&
                           meta.TryGetProperty("code", out var mc) ? mc.GetString() : null;
            JsonElement? result = root.TryGetProperty("result", out var r) ? r.Clone() : null;
            return (metaCode == "200", metaCode, result);
        }
        catch (JsonException)
        {
            return (false, "BAD_JSON", null);
        }
    }

    /// <summary>裸 JSON POST（sendMessageCodeBase），带 X-YP 头。</summary>
    private async Task<(bool Ok, string? Code, string? Message, JsonElement? Data)> PostRawJsonAsync(
        string url, object body, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json"),
        };
        msg.Headers.TryAddWithoutValidation("X-YP-Access-Token", _session.AccessToken ?? "");
        msg.Headers.TryAddWithoutValidation("access-token", _session.AccessToken ?? "");
        msg.Headers.TryAddWithoutValidation("X-YP-Client-Id", _session.ClientId);
        msg.Headers.TryAddWithoutValidation("client-id", _session.ClientId);
        using var resp = await _http.SendAsync(msg, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseRawEnvelope(text);
    }

    /// <summary>
    /// 解析裸接口响应。两种形态并存：
    /// 1. BFF：<c>{"meta":{"code":"200"},"result":…}</c>
    /// 2. api-user 裸接口：<c>{"STATUS":"200","RSP":{"RSP_CODE":"0000","RSP_DESC":"…","DATA":…}}</c>
    /// sendMessageCodeBase 属于第 2 种——之前只认 meta，导致「短信已收到却提示发送失败」。
    /// </summary>
    internal static (bool Ok, string? Code, string? Message, JsonElement? Data) ParseRawEnvelope(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.TryGetProperty("meta", out var meta) &&
                meta.TryGetProperty("code", out var mc))
            {
                var metaCode = mc.GetString();
                JsonElement? result = root.TryGetProperty("result", out var r) ? r.Clone() : null;

                // message 可能挂在根上，也可能挂在 meta 里
                string? metaMsg = null;
                foreach (var holder in new[] { root, meta })
                {
                    if (holder.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    {
                        metaMsg = m.GetString();
                        break;
                    }
                }
                return (metaCode == "200", metaCode, metaMsg, result);
            }

            if (root.TryGetProperty("RSP", out var rsp) && rsp.ValueKind == JsonValueKind.Object)
            {
                string? Str(string name) =>
                    rsp.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

                JsonElement? payload = rsp.TryGetProperty("DATA", out var dd) && dd.ValueKind != JsonValueKind.Null
                    ? dd.Clone() : null;

                var code = Str("RSP_CODE");
                return (code == "0000", code, Str("RSP_DESC"), payload);
            }

            return (false, "BAD_SHAPE", "响应格式异常", null);
        }
        catch (JsonException)
        {
            return (false, "BAD_JSON", "响应不是合法 JSON", null);
        }
    }

    /// <summary>从 api-user 原始信封提取 access_token（DATA 为 clientSecret AES 密文）。</summary>
    private (string Code, string? Token) ExtractToken(DispatcherResponse resp)
    {
        var code = resp.Rsp?.Code ?? "NO_RSP";
        if (resp.Rsp?.Data is not { } d)
            return (code, null);

        JsonElement data;
        if (d.ValueKind == JsonValueKind.String)
        {
            try
            {
                var plain = CryptoHelper.Decrypt(d.GetString()!, _session.ClientSecret);
                data = JsonDocument.Parse(plain).RootElement.Clone();
            }
            catch
            {
                return (code, null);
            }
        }
        else
        {
            data = d;
        }

        if (data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("access_token", out var t) &&
            t.ValueKind == JsonValueKind.String)
            return (code, t.GetString());

        return (code, null);
    }

    private static string Describe(DispatcherResponse resp) =>
        resp.Rsp?.Desc ?? resp.Msg ?? "未知错误";
}

// ---------- DTO ----------

public sealed class QrCodeTicket(string uuid, string imageBase64DataUri)
{
    public string Uuid { get; } = uuid;
    public string ImageDataUri { get; } = imageBase64DataUri;
}

public enum QrPollState { Waiting, Scanned, Confirmed }

/// <summary>密码登录需要短信二次验证（非错误，属正常流程分支）。</summary>
public sealed class NeedSmsVerifyException(string code, string message) : WoCloudException(code, message);

internal sealed class VerifyCodeShowData
{
    [JsonPropertyName("isNeedVerfyCode")] public int IsNeedVerifyCode { get; set; }
}

internal sealed class QrCodeData
{
    [JsonPropertyName("uuid")] public string? Uuid { get; set; }
    [JsonPropertyName("image")] public string? Image { get; set; }
    [JsonPropertyName("state")] public int? State { get; set; }
    [JsonPropertyName("token")] public string? Token { get; set; }
}
