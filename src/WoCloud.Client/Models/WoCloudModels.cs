using System.Text.Json.Serialization;

namespace WoCloud.Client.Models;

/// <summary>文件/目录项（QueryAllFiles files[]）。</summary>
public class CloudFile
{
    [JsonPropertyName("fid")] public string Fid { get; set; } = "";
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public int Type { get; set; }          // 1=文件 0=目录
    [JsonPropertyName("fileType")] public string FileType { get; set; } = "";  // 1图2视频3音4文档5其他
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("spaceType")] public string SpaceType { get; set; } = "";
    [JsonPropertyName("parentDirectoryId")] public string ParentDirectoryId { get; set; } = "";
    [JsonPropertyName("createTime")] public string CreateTime { get; set; } = "";
    [JsonPropertyName("modifyTime")] public string ModifyTime { get; set; } = "";
    [JsonPropertyName("shootingTime")] public string? ShootingTime { get; set; }
    [JsonPropertyName("thumbUrl")] public string? ThumbUrl { get; set; }
    [JsonPropertyName("previewUrl")] public string? PreviewUrl { get; set; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    [JsonPropertyName("isCollected")] public int IsCollected { get; set; }

    [JsonIgnore] public bool IsDirectory => Type == 0;
    /// <summary>是否有可用缩略图（图片/视频预览）。</summary>
    [JsonIgnore] public bool HasThumb => !IsDirectory && !string.IsNullOrEmpty(ThumbUrl);

    /// <summary>
    /// 图标分类：目录单独一类，文件按扩展名细分（服务端 fileType 只有 5 类，区分不出文档类型）。
    /// </summary>
    [JsonIgnore]
    private string Kind => IsDirectory
        ? "folder"
        : System.IO.Path.GetExtension(Name).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".heic" or ".svg" or ".tif" or ".tiff" => "image",
            ".mp4" or ".mkv" or ".mov" or ".avi" or ".wmv" or ".flv" or ".m4v" or ".webm" or ".rmvb" => "video",
            ".mp3" or ".flac" or ".wav" or ".aac" or ".m4a" or ".ogg" or ".wma" or ".ape" => "audio",
            ".pdf" => "pdf",
            ".doc" or ".docx" or ".rtf" or ".wps" => "word",
            ".xls" or ".xlsx" or ".csv" or ".et" => "excel",
            ".ppt" or ".pptx" or ".dps" => "ppt",
            ".txt" or ".md" or ".log" or ".json" or ".xml" or ".cs" or ".js" or ".ts" or ".html"
                or ".css" or ".yml" or ".yaml" or ".ini" or ".bat" or ".ps1" or ".sh" => "text",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".xz" or ".bz2" => "archive",
            ".exe" or ".msi" or ".apk" or ".dmg" or ".iso" or ".appx" => "app",
            _ => "other",
        };

    /// <summary>Segoe Fluent Icons 字形；文档类返回空串，改用扩展名角标（否则字形和角标会叠在一起）。</summary>
    [JsonIgnore] public string IconGlyph => Kind switch
    {
        "folder" => "\uE8B7",   // Folder
        "image" => "\uEB9F",    // Picture
        "video" => "\uE714",    // Video
        "audio" => "\uE8D6",    // Volume
        "other" => "\uE7C3",    // Page
        _ => "",
    };

    /// <summary>图标颜色（#RRGGBB），按类型区分，避免一列灰点分不清。</summary>
    [JsonIgnore] public string IconColor => Kind switch
    {
        "folder" => "#F2B01E",
        "image" => "#2D9CDB",
        "video" => "#9B51E0",
        "audio" => "#EB5757",
        "pdf" => "#E5484D",
        "word" => "#2F6FEB",
        "excel" => "#217346",
        "ppt" => "#D24726",
        "text" => "#6B7280",
        "archive" => "#8D6E63",
        "app" => "#4B5563",
        _ => "#8A94A0",
    };

    /// <summary>扩展名角标文字（文档/压缩包/程序），其余为空串不显示。</summary>
    [JsonIgnore] public string BadgeText => Kind switch
    {
        "pdf" => "PDF",
        "word" => "DOC",
        "excel" => "XLS",
        "ppt" => "PPT",
        "text" => "TXT",
        "archive" => "ZIP",
        "app" => "EXE",
        _ => "",
    };
    [JsonIgnore] public string SizeText => IsDirectory ? "" : FormatSize(Size);
    [JsonIgnore] public string TimeText => FormatTime(string.IsNullOrEmpty(ModifyTime) ? CreateTime : ModifyTime);

    /// <summary>字节数 → 可读容量（1024 进制，最高到 PB；整数时不带小数位）。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 0) return "-";
        const long kb = 1L << 10, mb = 1L << 20, gb = 1L << 30, tb = 1L << 40, pb = 1L << 50;

        static string Fmt(double v, string unit)
        {
            var s = v >= 100 ? v.ToString("F0") : v >= 10 ? v.ToString("F1") : v.ToString("F2");
            if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
            return s + " " + unit;
        }

        return bytes switch
        {
            >= pb => Fmt(bytes / (double)pb, "PB"),
            >= tb => Fmt(bytes / (double)tb, "TB"),
            >= gb => Fmt(bytes / (double)gb, "GB"),
            >= mb => Fmt(bytes / (double)mb, "MB"),
            >= kb => Fmt(bytes / (double)kb, "KB"),
            _ => bytes + " B",
        };
    }

    public static string FormatTime(string t) =>
        t.Length == 14 && DateTime.TryParseExact(t, "yyyyMMddHHmmss", null,
            System.Globalization.DateTimeStyles.None, out var dt)
            ? dt.ToString("yyyy-MM-dd HH:mm")
            : t;
}

/// <summary>回收站条目（QueryRecycleData）。</summary>
public class RecycleItem
{
    [JsonPropertyName("deleteNo")] public string DeleteNo { get; set; } = "";
    [JsonPropertyName("deleteTime")] public string DeleteTime { get; set; } = "";
    [JsonPropertyName("fid")] public string Fid { get; set; } = "";
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("fileSize")] public long FileSize { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("fileType")] public string? FileType { get; set; }
    [JsonPropertyName("thumbUrl")] public string? ThumbUrl { get; set; }
    [JsonPropertyName("keepDays")] public int KeepDays { get; set; }

    [JsonIgnore] public bool IsDirectory => Type == "0";
    [JsonIgnore] public bool HasThumb => !IsDirectory && !string.IsNullOrEmpty(ThumbUrl);
    [JsonIgnore] public string SizeText => IsDirectory ? "" : CloudFile.FormatSize(FileSize);
    [JsonIgnore] public string DeleteTimeText => CloudFile.FormatTime(DeleteTime);
    [JsonIgnore] public string KeepText => KeepDays > 0 ? $"{KeepDays} 天后清除" : "";
}

/// <summary>飞传条目（FlyPassFileList.list[]，字段取自前端模板）。</summary>
public class FlyPassItem
{
    [JsonPropertyName("flyPassId")] public string FlyPassId { get; set; } = "";
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("fileName")] public string FileName { get; set; } = "";
    [JsonPropertyName("num")] public System.Text.Json.JsonElement? Num { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("fromNickName")] public string? FromNickName { get; set; }
    [JsonPropertyName("fromUserId")] public string? FromUserId { get; set; }
    [JsonPropertyName("invalidTime")] public string? InvalidTime { get; set; }
    [JsonPropertyName("createTime")] public string? CreateTime { get; set; }
    [JsonPropertyName("thumbUrl")] public string? ThumbUrl { get; set; }

    [JsonIgnore] public bool HasThumb => !string.IsNullOrEmpty(ThumbUrl);
    [JsonIgnore] public string StatusText => Status switch
    {
        "1" => "待接收",
        "2" => "已接收",
        "3" => "已取消",
        "6" => "已失效",
        _ => "未知",
    };
    [JsonIgnore] public string ExpireText => CloudFile.FormatTime(InvalidTime ?? "");
}

/// <summary>飞传列表外层（FlyPassFileList DATA = {list}）。</summary>
public class FlyPassListResult
{
    [JsonPropertyName("list")] public List<FlyPassItem> List { get; set; } = [];
}

/// <summary>上传断点日志（QueryUploadLog DATA = {fileLogs, count}）。</summary>
public class UploadLogResult
{
    [JsonPropertyName("fileLogs")] public List<UploadLogItem> FileLogs { get; set; } = [];
    [JsonPropertyName("count")] public int Count { get; set; }
}

public class UploadLogItem
{
    [JsonPropertyName("fid")] public string Fid { get; set; } = "";
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("fileName")] public string FileName { get; set; } = "";
    [JsonPropertyName("fileSize")] public long FileSize { get; set; }
    [JsonPropertyName("createTime")] public string? CreateTime { get; set; }
    [JsonPropertyName("thumbUrl")] public string? ThumbUrl { get; set; }
    [JsonPropertyName("fileType")] public string? FileType { get; set; }
}

/// <summary>SearchFile 单项（服务端字段为 fileName/fileSize，与列表不同）。</summary>
public class SearchFileItem
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("fid")] public string? Fid { get; set; }
    [JsonPropertyName("fileName")] public string? FileName { get; set; }
    [JsonPropertyName("fileSize")] public long FileSize { get; set; }
    /// <summary>搜索结果的 type 是字符串（"0"/"1"/""），需自行解析。</summary>
    [JsonPropertyName("type")] public string? TypeRaw { get; set; }
    [JsonPropertyName("fileType")] public string? FileType { get; set; }
    [JsonPropertyName("thumbUrl")] public string? ThumbUrl { get; set; }
    [JsonPropertyName("previewUrl")] public string? PreviewUrl { get; set; }
    [JsonPropertyName("parentDirectoryId")] public string? ParentDirectoryId { get; set; }
    [JsonPropertyName("createTime")] public string? CreateTime { get; set; }
    [JsonPropertyName("modifyTime")] public string? ModifyTime { get; set; }

    [JsonIgnore] public int Type => int.TryParse(TypeRaw, out var t) ? t : 1;

    public CloudFile ToCloudFile() => new()
    {
        Id = Id ?? "",
        Fid = Fid ?? "",
        Name = FileName ?? "",
        Size = FileSize,
        Type = Type,
        FileType = FileType ?? "",
        ThumbUrl = ThumbUrl,
        PreviewUrl = PreviewUrl,
        ParentDirectoryId = ParentDirectoryId ?? "",
        CreateTime = CreateTime ?? "",
        ModifyTime = ModifyTime ?? "",
    };
}

/// <summary>SearchFile 响应（DATA = {personalResult, familyResult}）。</summary>
public class SearchResult
{
    [JsonPropertyName("personalResult")] public List<SearchFileItem> PersonalResult { get; set; } = [];
    [JsonPropertyName("familyResult")] public List<SearchFileItem> FamilyResult { get; set; } = [];

    [JsonIgnore]
    public List<CloudFile> Files =>
        [.. PersonalResult.Select(x => x.ToCloudFile()), .. FamilyResult.Select(x => x.ToCloudFile())];
}

public class QueryAllFilesResult
{
    [JsonPropertyName("files")] public List<CloudFile> Files { get; set; } = [];
    [JsonPropertyName("systemDirs")] public List<CloudDirectory> SystemDirs { get; set; } = [];
    /// <summary>接口不返回 total，用列表长度判断是否还有下一页。</summary>
    [JsonIgnore] public int Total => Files.Count;
}

public class QueryDirectorysResult
{
    [JsonPropertyName("directorys")] public List<CloudDirectory> Directorys { get; set; } = [];
}

public class CloudDirectory
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("directoryName")] public string DirectoryName { get; set; } = "";
    [JsonPropertyName("creator")] public string? Creator { get; set; }
    [JsonPropertyName("createTime")] public string? CreateTime { get; set; }
    [JsonPropertyName("allowDelete")] public string? AllowDelete { get; set; }
}

/// <summary>用户信息（AppQueryUser DATA）。</summary>
public class CloudUserInfo
{
    [JsonPropertyName("userId")] public string UserId { get; set; } = "";
    [JsonPropertyName("realUserId")] public string? RealUserId { get; set; }
    [JsonPropertyName("userName")] public string? UserName { get; set; }
    [JsonPropertyName("headUrl")] public string? HeadUrl { get; set; }
    [JsonPropertyName("sex")] public string? Sex { get; set; }
    [JsonPropertyName("birthday")] public string? Birthday { get; set; }
    [JsonPropertyName("isSetPassword")] public string? IsSetPassword { get; set; }
    [JsonPropertyName("registerTime")] public string? RegisterTime { get; set; }
}

/// <summary>下载直链项（GetDownloadUrl DATA[i]）。</summary>
public class DownloadUrlItem
{
    [JsonPropertyName("fid")] public string? Fid { get; set; }
    [JsonPropertyName("downloadUrl")] public string DownloadUrl { get; set; } = "";
}

/// <summary>我的分享列表项（AllShareFileList list[]）。</summary>
public class ShareItem
{
    [JsonPropertyName("shareId")] public string? ShareId { get; set; }
    [JsonPropertyName("shareUrl")] public string? ShareUrl { get; set; }
    [JsonPropertyName("shareShortUrl")] public string? ShareShortUrl { get; set; }
    [JsonPropertyName("shareCode")] public string? ShareCode { get; set; }
    [JsonPropertyName("fileName")] public string? FileName { get; set; }
    [JsonPropertyName("fid")] public string? Fid { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    /// <summary>文件数量（服务端时而数字时而字符串，用 JsonElement 兜底，未参与业务逻辑）。</summary>
    [JsonPropertyName("num")] public System.Text.Json.JsonElement? Num { get; set; }
    /// <summary>类型（同上，用 JsonElement 兜底）。</summary>
    [JsonPropertyName("type")] public System.Text.Json.JsonElement? Type { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("shareTime")] public string? ShareTime { get; set; }
    [JsonPropertyName("invalidTime")] public string? InvalidTime { get; set; }
    [JsonPropertyName("thumbUrl")] public string? ThumbUrl { get; set; }

    [JsonIgnore] public string ShareName => FileName ?? "";
    [JsonIgnore] public string CreateTime => CloudFile.FormatTime(ShareTime ?? "");
    [JsonIgnore] public string ExpireTime => CloudFile.FormatTime(InvalidTime ?? "");
    [JsonIgnore] public string Link => ShareShortUrl ?? ShareUrl ?? "";
}

/// <summary>我的分享列表外层（AllShareFileList DATA）。</summary>
public class ShareListResult
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("list")] public List<ShareItem> List { get; set; } = [];
}

/// <summary>BFF 通用包装 {meta, result}。</summary>
public class BffEnvelope<T>
{
    [JsonPropertyName("meta")] public BffMeta? Meta { get; set; }
    [JsonPropertyName("result")] public T? Result { get; set; }
}

public class BffMeta
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("message")] public string? Message { get; set; }
}

/// <summary>BFF 会员卡（/api/bff/member/info/call/card result）。</summary>
public class MemberCard
{
    [JsonPropertyName("vipLevel")] public int VipLevel { get; set; }
    [JsonPropertyName("vipName")] public string? VipName { get; set; }
    [JsonPropertyName("validDate")] public string? ValidDate { get; set; }
    [JsonPropertyName("usedSpace")] public long UsedSpace { get; set; }
    [JsonPropertyName("allSpace")] public long AllSpace { get; set; }
    [JsonPropertyName("vipUsedSpace")] public long? VipUsedSpace { get; set; }
    [JsonPropertyName("vipAllSpace")] public long? VipAllSpace { get; set; }
    [JsonPropertyName("space")] public string? Space { get; set; }
    [JsonPropertyName("button")] public string? Button { get; set; }
}

/// <summary>会员摘要（BFF card/shouting 实测字段）。</summary>
public class MemberSummary
{
    [JsonPropertyName("vipLevel")] public int VipLevel { get; set; }
    [JsonPropertyName("needDisplayedVipLevel")] public int NeedDisplayedVipLevel { get; set; }
    [JsonPropertyName("vipDesc")] public string? VipDesc { get; set; }
    [JsonPropertyName("validDate")] public string? ValidDate { get; set; }
    [JsonPropertyName("usedSpace")] public long UsedSpace { get; set; }
    [JsonPropertyName("allSpace")] public long AllSpace { get; set; }
    [JsonPropertyName("headImageUrl")] public string? HeadImageUrl { get; set; }
    [JsonPropertyName("isContinuous")] public int IsContinuous { get; set; }
    [JsonPropertyName("syncMemberDesc")] public string? SyncMemberDesc { get; set; }

    // BFF card/shouting 的 usedSpace / allSpace 单位是 KB（实测：2147483648 → 2 TB、1405781 → 1.34 GB，
    // 而账号内仅图片就有 58 MB，若按字节解释明显矛盾），统一换算成字节再格式化。
    [JsonIgnore] public long UsedBytes => UsedSpace * 1024;
    [JsonIgnore] public long AllBytes => AllSpace * 1024;

    [JsonIgnore] public double UsedRatio => AllBytes > 0 ? (double)UsedBytes / AllBytes : 0;
    [JsonIgnore] public string UsedText => CloudFile.FormatSize(UsedBytes);
    [JsonIgnore] public string AllText => CloudFile.FormatSize(AllBytes);
    [JsonIgnore] public string LevelText => VipDesc ?? (VipLevel > 0 ? $"会员 Lv{VipLevel}" : "普通用户");
    [JsonIgnore] public string RenewText => IsContinuous == 1 ? "自动续费中" : "未开启自动续费";
}
