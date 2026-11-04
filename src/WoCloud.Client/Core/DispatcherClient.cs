using System.Text;
using System.Text.Json;

namespace WoCloud.Client.Core;

/// <summary>
/// dispatcher 协议客户端：全部 55+ 业务接口的公共入口。
/// 职责：构建信封（key/resTime/reqSeq/sign）→ AES 加密 param → POST → 解密 RSP.DATA → 错误码处理。
/// </summary>
public sealed class DispatcherClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // 服务端经常把数字当字符串返回（如 "type":"1"、"num":"1"），统一兼容
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly HttpClient _http;
    private readonly SessionState _session;
    private readonly Random _random = new();

    public DispatcherClient(SessionState session, HttpClient? httpClient = null)
    {
        _session = session;
        _http = httpClient ?? new HttpClient();
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://pan.wo.cn");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://pan.wo.cn/");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
    }

    private string ChannelUrl(string channel) => channel == WoCloudConstants.ChannelApiUser
        ? WoCloudConstants.ApiUserDispatcher
        : WoCloudConstants.WohomeDispatcher;

    private string ResolveSecret(string channel, SecretType secretType)
    {
        if (secretType == SecretType.ClientSecret || channel == WoCloudConstants.ChannelApiUser)
            return _session.ClientSecret;
        return _session.AccessToken ?? throw new InvalidOperationException("未登录（wohome 通道需要 accessToken）");
    }

    /// <summary>
    /// 调用 dispatcher 接口。
    /// </summary>
    /// <param name="key">业务方法名，如 QueryAllFiles</param>
    /// <param name="param">业务参数对象（将 JSON 序列化后整体 AES 加密）</param>
    /// <param name="channel">wohome / api-user</param>
    /// <param name="secretType">密钥选择策略</param>
    /// <param name="encrypt">false 时不加密（明文 body，如 FCloudProductOrdListQry）</param>
    public async Task<TData?> CallAsync<TData>(
        string key, object? param = null,
        string channel = WoCloudConstants.ChannelWohome,
        SecretType secretType = SecretType.Token,
        bool encrypt = true,
        CancellationToken ct = default)
    {
        var response = await SendRawAsync(key, param, channel, secretType, encrypt, ct).ConfigureAwait(false);

        var rsp = response.Rsp ?? throw new WoCloudException("NO_RSP", "响应缺少 RSP 节点", response.LogId);
        var code = rsp.Code;

        // 会话失效 → 触发事件并抛异常
        if (code is "130007" or "130008" or "1001")
        {
            _session.NotifySessionExpired();
            throw new WoCloudException(code, rsp.Desc ?? "会话失效", response.LogId);
        }
        if (code != "0000")
            throw new WoCloudException(code, rsp.Desc ?? "未知错误", response.LogId);

        if (rsp.Data is not { } data || data.ValueKind == JsonValueKind.Null)
            return default;

        // DATA 为 base64 密文时解密；否则已是明文 JSON 对象
        TData? result;
        if (encrypt && data.ValueKind == JsonValueKind.String)
        {
            var secret = ResolveSecret(channel, secretType);
            var plain = CryptoHelper.Decrypt(data.GetString()!, secret);
            // 部分接口（删除/清空等）成功时 DATA 解密后为空串，直接返回默认值
            result = string.IsNullOrWhiteSpace(plain)
                ? default
                : JsonSerializer.Deserialize<TData>(plain, JsonOpts);
        }
        else
        {
            result = data.Deserialize<TData>(JsonOpts);
        }
        return result;
    }

    /// <summary>发送请求并返回原始信封响应（供登录流程等特殊处理）。</summary>
    public async Task<DispatcherResponse> SendRawAsync(
        string key, object? param,
        string channel = WoCloudConstants.ChannelWohome,
        SecretType secretType = SecretType.Token,
        bool encrypt = true,
        CancellationToken ct = default)
    {
        var resTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var reqSeq = _random.Next(100000, 999999);
        var sign = CryptoHelper.Md5Hex(key + resTime + reqSeq + channel + "");

        JsonElement envelope;
        if (encrypt)
        {
            var body = new DispatcherBody();
            var secretKey = ResolveSecret(channel, secretType);
            var plain = param is null ? "{}" : JsonSerializer.Serialize(param, JsonOpts);
            body.Param = CryptoHelper.Encrypt(plain, secretKey);
            if (channel == WoCloudConstants.ChannelApiUser)
            {
                body.Secret = true;
                body.ClientId = _session.ClientId;
            }
            else
            {
                body.Key = true; // wohome 前端使用 "key": true 标志位
                if (secretType == SecretType.ClientSecret)
                {
                    // 分享查看场景：clientSecret 加密 + 显式声明密钥类型与 clientId（实测必需）
                    body.SecretType = "clientSecret";
                    body.ClientId = _session.ClientId;
                }
            }
            envelope = JsonSerializer.SerializeToElement(new DispatcherRequest
            {
                Header = new DispatcherHeader
                {
                    Key = key, ResTime = resTime, ReqSeq = reqSeq,
                    Channel = channel, Version = "", Sign = sign,
                },
                Body = body,
            }, JsonOpts);
        }
        else
        {
            // 明文模式：param 字段平铺进 body（如 FCloudProductOrdListQry）
            var header = JsonSerializer.SerializeToElement(new DispatcherHeader
            {
                Key = key, ResTime = resTime, ReqSeq = reqSeq,
                Channel = channel, Version = "", Sign = sign,
            }, JsonOpts);
            var bodyObj = new System.Text.Json.Nodes.JsonObject();
            if (param is not null)
            {
                var p = JsonSerializer.SerializeToElement(param, JsonOpts);
                if (p.ValueKind == JsonValueKind.Object)
                    foreach (var prop in p.EnumerateObject())
                        bodyObj[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());
            }
            envelope = new System.Text.Json.Nodes.JsonObject
            {
                ["header"] = System.Text.Json.Nodes.JsonNode.Parse(header.GetRawText()),
                ["body"] = bodyObj,
            }.Deserialize<JsonElement>();
        }

        using var content = new StringContent(
            envelope.GetRawText(), Encoding.UTF8, "application/json");
        using var msg = new HttpRequestMessage(HttpMethod.Post, ChannelUrl(channel)) { Content = content };
        if (!string.IsNullOrEmpty(_session.AccessToken))
            msg.Headers.TryAddWithoutValidation("accesstoken", _session.AccessToken);
        using var resp = await _http.SendAsync(msg, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        DispatcherResponse? parsed;
        try { parsed = JsonSerializer.Deserialize<DispatcherResponse>(text); }
        catch (JsonException e)
        {
            // 服务端限流/过载时会返回 HTML 错误页，给出可读提示
            var hint = text.TrimStart().StartsWith('<') ? "服务端返回了非 JSON 页面（可能限流或繁忙），请稍后重试" : e.Message;
            throw new WoCloudException("BAD_JSON", "响应解析失败：" + hint);
        }

        if (parsed?.Rsp is null)
        {
            var status = parsed?.Status ?? resp.StatusCode.ToString();
            throw new WoCloudException("HTTP_ERR", $"HTTP {(int)resp.StatusCode} / STATUS={status}: {Truncate(text, 200)}");
        }
        return parsed;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    public void Dispose() => _http.Dispose();
}
