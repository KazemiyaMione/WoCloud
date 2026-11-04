using System.Text.Json;
using System.Text.Json.Serialization;
using WoCloud.Client.Models;

namespace WoCloud.Client.Member;

/// <summary>
/// 会员权益（BFF <c>/api/bff/membershipBenefit/V2/displayMembershipBenefit</c>，
/// result 为 accessToken 加密的密文）。服务端数字都以字符串下发，统一用 AllowReadingFromString。
/// </summary>
public sealed class MembershipBenefit
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    [JsonPropertyName("vipDesc")] public string? VipDesc { get; set; }
    [JsonPropertyName("vipLevel")] public string? VipLevel { get; set; }
    [JsonPropertyName("vipWeight")] public long VipWeight { get; set; }

    /// <summary>总空间（MB）。实测 2097152 = 2 TB。</summary>
    [JsonPropertyName("capacity")] public long CapacityMb { get; set; }

    /// <summary>单文件上传上限（MB）。实测 5120 = 5 GB。</summary>
    [JsonPropertyName("uploadFileSize")] public long UploadFileSizeMb { get; set; }

    /// <summary>回收站保留天数。</summary>
    [JsonPropertyName("recycleBin")] public long RecycleBinDays { get; set; }

    /// <summary>可创建/加入的家庭数。</summary>
    [JsonPropertyName("familyCount")] public long FamilyCount { get; set; }

    /// <summary>家庭人数上限。</summary>
    [JsonPropertyName("memberCount")] public long MemberCount { get; set; }

    /// <summary>快传/闪传每日次数。</summary>
    [JsonPropertyName("transientCount")] public long TransientCount { get; set; }

    /// <summary>单次批量上传文件数。</summary>
    [JsonPropertyName("batchUploadCount")] public long BatchUploadCount { get; set; }

    [JsonPropertyName("applyTime")] public string? ApplyTime { get; set; }
    [JsonPropertyName("expireTime")] public string? ExpireTime { get; set; }

    // ---------------- 展示用 ----------------

    [JsonIgnore] public string UploadLimitText =>
        UploadFileSizeMb <= 0 ? "—" : CloudFile.FormatSize(UploadFileSizeMb * 1024L * 1024);

    [JsonIgnore] public string RecycleText => RecycleBinDays <= 0 ? "—" : $"{RecycleBinDays} 天";

    [JsonIgnore] public string FamilyText => FamilyCount <= 0 ? "—" : $"{FamilyCount} 个 / {MemberCount} 人";

    [JsonIgnore] public string TransientText => TransientCount <= 0 ? "—" : $"{TransientCount} 次/天";

    [JsonIgnore] public string BatchText => BatchUploadCount <= 0 ? "—" : $"{BatchUploadCount} 个/次";

    [JsonIgnore] public string ExpireText => string.IsNullOrWhiteSpace(ExpireTime) ? "—" : ExpireTime!;
}
