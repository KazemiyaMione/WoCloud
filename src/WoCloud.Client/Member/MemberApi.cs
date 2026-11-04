using System.Text.Json;
using WoCloud.Client.Core;
using WoCloud.Client.Models;

namespace WoCloud.Client.Member;

/// <summary>
/// BFF 会员接口（不走 dispatcher）：
/// 头 = X-YP-Access-Token + X-YP-Client-Id + client-id；响应 {meta:{code},result}。
/// </summary>
public sealed class MemberApi
{
    private readonly HttpClient _http;
    private readonly SessionState _session;

    public MemberApi(SessionState session, HttpClient? httpClient = null)
    {
        _session = session;
        _http = httpClient ?? new HttpClient();
    }

    /// <summary>会员卡/空间用量（BFF，部分 token 类型返回 500，失败时请改用 <see cref="GetMemberSummaryAsync"/>）。</summary>
    public async Task<MemberCard?> GetMemberCardAsync(CancellationToken ct = default)
    {
        var env = await PostBffAsync<MemberCard>("/api/bff/member/info/call/card", new { }, ct)
            .ConfigureAwait(false);
        return env?.Result;
    }

    /// <summary>
    /// 会员摘要（实测稳定可用）：BFF <c>/api/bff/member/info/card/shouting</c>。
    /// 返回 vipLevel / vipDesc / validDate / usedSpace / allSpace / headImageUrl / isContinuous。
    /// </summary>
    public async Task<MemberSummary?> GetMemberSummaryAsync(CancellationToken ct = default)
    {
        var env = await PostBffAsync<MemberSummary>("/api/bff/member/info/card/shouting", new { }, ct)
            .ConfigureAwait(false);
        return env?.Result;
    }

    /// <summary>会员口播/引导文案。</summary>
    public async Task<JsonElement?> GetShoutingAsync(CancellationToken ct = default)
    {
        var env = await PostBffAsync<JsonElement>("/api/bff/member/info/card/shouting", new { }, ct)
            .ConfigureAwait(false);
        return env?.Result;
    }

    /// <summary>会员权益展示（result 为 clientSecret AES 密文，自动解密）。</summary>
    public async Task<JsonElement?> GetMembershipBenefitAsync(CancellationToken ct = default)
    {
        using var msg = BuildRequest(HttpMethod.Post, "/api/bff/membershipBenefit/V2/displayMembershipBenefit", "{}");
        using var resp = await _http.SendAsync(msg, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (!root.TryGetProperty("meta", out var meta) || meta.GetProperty("code").GetString() != "200")
            return null;
        if (!root.TryGetProperty("result", out var result))
            return null;
        if (result.ValueKind == JsonValueKind.String)
        {
            // 实测：这个接口的 result 用 accessToken 加密（不是 clientSecret，用 clientSecret 会 bad decrypt）
            var cipher = result.GetString()!;
            string? plain = null;
            if (!string.IsNullOrEmpty(_session.AccessToken))
            {
                try { plain = CryptoHelper.Decrypt(cipher, _session.AccessToken); }
                catch { plain = null; }
            }
            plain ??= CryptoHelper.Decrypt(cipher, _session.ClientSecret); // 兼容旧行为
            return JsonDocument.Parse(plain).RootElement.Clone();
        }
        return result.Clone();
    }

    /// <summary>会员权益（强类型）：上传上限 / 回收站天数 / 家庭人数 / 快传次数 等。</summary>
    public async Task<MembershipBenefit?> GetBenefitAsync(CancellationToken ct = default)
    {
        var el = await GetMembershipBenefitAsync(ct).ConfigureAwait(false);
        if (el is not { } e) return null;
        try
        {
            return e.Deserialize<MembershipBenefit>(MembershipBenefit.JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, string jsonBody)
    {
        var msg = new HttpRequestMessage(method, WoCloudConstants.BaseUrl + path)
        {
            Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json"),
        };
        msg.Headers.TryAddWithoutValidation("X-YP-Access-Token", _session.AccessToken ?? "");
        msg.Headers.TryAddWithoutValidation("X-YP-Client-Id", _session.ClientId);
        msg.Headers.TryAddWithoutValidation("client-id", _session.ClientId);
        return msg;
    }

    private async Task<BffEnvelope<T>?> PostBffAsync<T>(string path, object body, CancellationToken ct)
    {
        using var msg = BuildRequest(HttpMethod.Post, path,
            System.Text.Json.JsonSerializer.Serialize(body));
        using var resp = await _http.SendAsync(msg, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return System.Text.Json.JsonSerializer.Deserialize<BffEnvelope<T>>(text);
    }
}
