using WoCloud.Client.Core;
using WoCloud.Client.Models;

namespace WoCloud.Client.Share;

/// <summary>
/// 分享管理（wohome 通道）：
/// fileIds / fileFolderIds 必须是<b>逗号分隔字符串</b>（数组会返回 RSP_CODE=1000）。
/// </summary>
public sealed class ShareApi
{
    private readonly DispatcherClient _dispatcher;
    private readonly SessionState _session;

    public ShareApi(DispatcherClient dispatcher, SessionState session)
    {
        _dispatcher = dispatcher;
        _session = session;
    }

    private string ClientId => _session.ClientId;

    /// <summary>我的分享列表（DATA = {total, list}）。</summary>
    public Task<ShareListResult?> ListMySharesAsync(int pageNum = 0, int pageSize = 50,
        CancellationToken ct = default) =>
        _dispatcher.CallAsync<ShareListResult>("AllShareFileList", new
        {
            clientId = ClientId,
            pageNum,
            pageSize,
        }, ct: ct);

    /// <summary>
    /// 创建分享。返回 DATA 含 shareUrl / shareCode / shareId。
    /// </summary>
    /// <param name="fileIds">文件 id 列表（文件对象的 id，不是 fid）</param>
    /// <param name="dirIds">目录 id 列表</param>
    /// <param name="days">有效期天数（1/7/30 等）</param>
    /// <param name="autoFill">"1"=系统生成提取码 "2"=无提取码</param>
    public Task<ShareItem?> CreateShareAsync(IEnumerable<string> fileIds,
        IEnumerable<string>? dirIds = null, int days = 7, string autoFill = "1",
        string spaceType = "0", CancellationToken ct = default) =>
        _dispatcher.CallAsync<ShareItem>("ShareFile", new
        {
            clientId = ClientId,
            spaceType,
            fileIds = string.Join(",", fileIds),
            fileFolderIds = string.Join(",", dirIds ?? []),
            days,
            autoFill,
        }, ct: ct);

    /// <summary>
    /// 取消/删除我的分享（实测通过）：{clientId, shareId:"逗号串", operType}。
    /// </summary>
    /// <param name="operType">1=取消分享，2=删除分享</param>
    public Task CancelShareAsync(IEnumerable<string> shareIds, int operType = 2, CancellationToken ct = default) =>
        _dispatcher.CallAsync<object>("CancelOrDelShareFile", new
        {
            clientId = ClientId,
            shareId = string.Join(",", shareIds),
            operType,
        }, ct: ct);

    /// <summary>
    /// 查询分享信息（实测）：clientSecret 通道 + <c>{shareId}</c>，
    /// 返回分享者昵称/头像/有效期等（无需登录，提取码校验在下一步）。
    /// </summary>
    public Task<ShareOwnerInfo?> GetShareInfoAsync(string shareId, CancellationToken ct = default) =>
        _dispatcher.CallAsync<ShareOwnerInfo>("QueryShareInfo", new { shareId },
            WoCloudConstants.ChannelWohome, SecretType.ClientSecret, ct: ct);

    /// <summary>
    /// 分享内文件列表（实测）：clientSecret 通道 + <c>{shareId, shareCode}</c>。
    /// 提取码错误时服务端返回非 0000。
    /// </summary>
    public Task<List<ShareFileEntry>?> GetShareFilesAsync(string shareId, string shareCode,
        CancellationToken ct = default) =>
        _dispatcher.CallAsync<List<ShareFileEntry>>("ShareFileDetail",
            new { shareId, shareCode },
            WoCloudConstants.ChannelWohome, SecretType.ClientSecret, ct: ct);

    /// <summary>
    /// 分享内的子目录（仅目录型分享）：token 通道 + <c>{clientId, directoryId, id:shareId}</c>。
    /// 文件型分享调用会返回 130006。
    /// </summary>
    public Task<QueryAllFilesResult?> QueryShareFolderAsync(string shareId, string directoryId = "0",
        string spaceType = "0", CancellationToken ct = default) =>
        _dispatcher.CallAsync<QueryAllFilesResult>("QueryShareFiles", new
        {
            clientId = ClientId,
            spaceType,
            directoryId,
            id = shareId,
        }, ct: ct);


    /// <summary>转存他人分享：{clientId, shareId, spaceType, targetDirId, dirList, fileList}。</summary>
    public Task ReceiveShareAsync(string shareId, string targetDirId,
        IEnumerable<string>? dirIds = null, IEnumerable<string>? fileIds = null,
        string spaceType = "0", CancellationToken ct = default) =>
        _dispatcher.CallAsync<object>("ReceiveShare", new
        {
            clientId = ClientId,
            shareId,
            spaceType,
            targetDirId,
            dirList = dirIds?.ToList() ?? [],
            fileList = fileIds?.ToList() ?? [],
        }, ct: ct);
}

/// <summary>分享信息（QueryShareInfo 实测字段）。</summary>
public sealed class ShareOwnerInfo
{
    [System.Text.Json.Serialization.JsonPropertyName("nickName")] public string? NickName { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("vipLevel")] public int VipLevel { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("imgUrl")] public string? ImgUrl { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("status")] public string? Status { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("invalidTime")] public string? InvalidTime { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("shareUserId")] public string? ShareUserId { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("invalid")] public bool Invalid { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public string ExpireText => Models.CloudFile.FormatTime(InvalidTime ?? "");
    [System.Text.Json.Serialization.JsonIgnore] public string StateText => Invalid ? "已失效" : "有效";
}

/// <summary>分享内文件（ShareFileDetail 实测字段，fileSize/type 均为字符串）。</summary>
public sealed class ShareFileEntry
{
    [System.Text.Json.Serialization.JsonPropertyName("id")] public string Id { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("fid")] public string? Fid { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("fileName")] public string FileName { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("fileSize")] public string? FileSize { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("fileType")] public string? FileType { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("type")] public string? Type { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("thumbUrl")] public string? ThumbUrl { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("createTime")] public string? CreateTime { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("spaceType")] public string? SpaceType { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public bool IsDirectory => Type == "0";
    [System.Text.Json.Serialization.JsonIgnore] public bool HasThumb => !IsDirectory && !string.IsNullOrEmpty(ThumbUrl);
    [System.Text.Json.Serialization.JsonIgnore]
    public string SizeText => IsDirectory ? "" :
        Models.CloudFile.FormatSize(long.TryParse(FileSize, out var v) ? v : 0);

    public Models.CloudFile ToCloudFile() => new()
    {
        Id = Id,
        Fid = Fid ?? "",
        Name = FileName,
        Size = long.TryParse(FileSize, out var v) ? v : 0,
        Type = int.TryParse(Type, out var t) ? t : 1,
        FileType = FileType ?? "",
        ThumbUrl = ThumbUrl,
    };
}
