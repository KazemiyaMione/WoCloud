using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WoCloud.Client.Core;

namespace WoCloud.Client.Misc;

/// <summary>
/// 其余已实测的辅助接口 + 通用「原始调用」（接口实验室用）。
/// 实测通过：ClassifyRule / QueryMuid / QueryAlbumBackupDeviceList / QueryWjsyBackup / QueryFamilyGroups。
/// </summary>
public sealed class MiscApi
{
    private readonly DispatcherClient _dispatcher;
    private readonly SessionState _session;

    public MiscApi(DispatcherClient dispatcher, SessionState session)
    {
        _dispatcher = dispatcher;
        _session = session;
    }

    private string ClientId => _session.ClientId;

    /// <summary>文件类型分类规则（扩展名 → type/subType/ability）。</summary>
    public Task<ClassifyRuleResult?> GetClassifyRuleAsync(CancellationToken ct = default) =>
        _dispatcher.CallAsync<ClassifyRuleResult>("ClassifyRule", new { clientId = ClientId }, ct: ct);

    /// <summary>设备/用户标识 muid。</summary>
    public Task<MuidResult?> GetMuidAsync(CancellationToken ct = default) =>
        _dispatcher.CallAsync<MuidResult>("QueryMuid", new { clientId = ClientId }, ct: ct);

    /// <summary>相册备份设备列表（DATA 是数组）。</summary>
    public Task<List<BackupDevice>?> GetAlbumBackupDevicesAsync(CancellationToken ct = default) =>
        _dispatcher.CallAsync<List<BackupDevice>>("QueryAlbumBackupDeviceList",
            new { clientId = ClientId }, ct: ct);

    /// <summary>微信小程序备份列表（DATA 是数组）。</summary>
    public Task<List<JsonElement>?> GetWxsyBackupAsync(int pageNum = 0, int pageSize = 20,
        CancellationToken ct = default) =>
        _dispatcher.CallAsync<List<JsonElement>>("QueryWjsyBackup", new
        {
            clientId = ClientId,
            pageNum,
            pageSize,
        }, ct: ct);

    /// <summary>家庭云群组（api-user 通道，需 accessToken 参数）。</summary>
    public Task<FamilyGroupsResult?> GetFamilyGroupsAsync(CancellationToken ct = default) =>
        _dispatcher.CallAsync<FamilyGroupsResult>("QueryFamilyGroups", new
        {
            accessToken = _session.AccessToken,
        }, WoCloudConstants.ChannelApiUser, SecretType.ClientSecret, ct: ct);

    /// <summary>
    /// 原始调用：接口实验室用，直接给 key + JSON 参数，返回原始 DATA 文本。
    /// 用于覆盖尚未封装成方法名的接口（不改动服务端返回结构）。
    /// </summary>
    public async Task<RawCallResult> InvokeRawAsync(string key, string paramJson,
        bool encrypt = true, string channel = WoCloudConstants.ChannelWohome,
        SecretType secretType = SecretType.Token, CancellationToken ct = default)
    {
        object? param = null;
        if (!string.IsNullOrWhiteSpace(paramJson))
        {
            var node = JsonNode.Parse(paramJson);
            param = node?.AsObject().ToDictionary(kv => kv.Key, kv => (object?)kv.Value?.ToJsonString());
        }

        var resp = await _dispatcher.SendRawAsync(key, param, channel, secretType, encrypt, ct)
            .ConfigureAwait(false);
        var rsp = resp.Rsp;
        string? data = rsp?.Data is { } d
            ? d.ValueKind == JsonValueKind.String ? d.GetString() : d.GetRawText()
            : null;
        return new RawCallResult(rsp?.Code ?? "", rsp?.Desc, data, resp.LogId);
    }
}

public sealed record RawCallResult(string Code, string? Desc, string? Data, string? LogId)
{
    public bool Ok => Code == "0000";
}

public sealed class ClassifyRuleResult
{
    /// <summary>扩展名 → 分类规则。</summary>
    [JsonPropertyName("fileTypes")] public Dictionary<string, ClassifyType> FileTypes { get; set; } = [];
}

public sealed class ClassifyType
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("subType")] public string? SubType { get; set; }
    [JsonPropertyName("ability")] public string? Ability { get; set; }
}

public sealed class MuidResult
{
    [JsonPropertyName("muid")] public string? Muid { get; set; }
}

public sealed class BackupDevice
{
    [JsonPropertyName("deviceId")] public string? DeviceId { get; set; }
    [JsonPropertyName("deviceName")] public string? DeviceName { get; set; }
    [JsonPropertyName("crateTime")] public string? CreateTime { get; set; }
    [JsonPropertyName("lastBackupTime")] public string? LastBackupTime { get; set; }
}

public sealed class FamilyGroupsResult
{
    [JsonPropertyName("iCreateGroups")] public List<FamilyGroup> Created { get; set; } = [];
    [JsonPropertyName("iJoinGroups")] public List<FamilyGroup> Joined { get; set; } = [];

    [JsonIgnore]
    public IEnumerable<FamilyGroup> All => Created.Concat(Joined);
}

public sealed class FamilyGroup
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("groupName")] public string? GroupName { get; set; }
    [JsonPropertyName("groupHeadUrl")] public string? GroupHeadUrl { get; set; }
    [JsonPropertyName("isDefault")] public string? IsDefault { get; set; }
    [JsonPropertyName("createTime")] public string? CreateTime { get; set; }
    [JsonPropertyName("groupMembers")] public List<FamilyMember> Members { get; set; } = [];

    [JsonIgnore] public string IdText => Id.ToString();
    [JsonIgnore] public string MemberCountText => $"{Members.Count} 位成员";
}

public sealed class FamilyMember
{
    [JsonPropertyName("memberName")] public string? MemberName { get; set; }
    [JsonPropertyName("memberHeadUrl")] public string? MemberHeadUrl { get; set; }
    [JsonPropertyName("memberRole")] public string? MemberRole { get; set; }
    [JsonPropertyName("memberId")] public string? MemberId { get; set; }
}
