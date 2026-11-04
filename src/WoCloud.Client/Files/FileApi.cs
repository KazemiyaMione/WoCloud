using WoCloud.Client.Core;
using WoCloud.Client.Models;

namespace WoCloud.Client.Files;

/// <summary>
/// 文件与目录操作（wohome 通道，token 加密）。
/// 参数命名与类型由服务端约定，改动需同步调整模型。
/// 注意：所有“操作类”接口使用文件的 <c>id</c>（目录同理）；
/// 仅 <c>GetDownloadUrl</c> 使用 <c>fid</c>（缩略图/下载令牌）。
/// </summary>
public sealed class FileApi
{
    private readonly DispatcherClient _dispatcher;
    private readonly SessionState _session;

    public FileApi(DispatcherClient dispatcher, SessionState session)
    {
        _dispatcher = dispatcher;
        _session = session;
    }

    private string ClientId => _session.ClientId;

    /// <summary>
    /// 列出目录内容（返回 {files, systemDirs}，无 total）。
    /// spaceType：0 个人云 / 1 家庭云（需 familyId）/ 4 私密空间（需 psToken）。
    /// </summary>
    public Task<QueryAllFilesResult?> ListAsync(string parentDirectoryId = "0",
        int pageNum = 0, int pageSize = 50, int sortRule = 0, string spaceType = "0",
        string? familyId = null, string? psToken = null, CancellationToken ct = default) =>
        _dispatcher.CallAsync<QueryAllFilesResult>("QueryAllFiles",
            BuildListParam(parentDirectoryId, pageNum, pageSize, sortRule, spaceType, familyId, psToken),
            ct: ct);

    /// <summary>构造列目录参数（个人云/家庭云/私密空间三种形态）。</summary>
    private object BuildListParam(string parentDirectoryId, int pageNum, int pageSize,
        int sortRule, string spaceType, string? familyId, string? psToken)
    {
        if (spaceType == "1")
            return new
            {
                clientId = ClientId,
                spaceType,
                familyId = familyId ?? "0",
                parentDirectoryId,
                pageNum,
                pageSize,
                sortRule,
            };
        if (spaceType == "4")
            return new
            {
                clientId = ClientId,
                spaceType,
                psToken = psToken ?? _session.PsToken ?? "",
                parentDirectoryId,
                pageNum,
                pageSize,
                sortRule,
            };
        return new
        {
            clientId = ClientId,
            spaceType,
            parentDirectoryId,
            pageNum,
            pageSize,
            sortRule,
        };
    }

    /// <summary>分页枚举目录下全部文件（接口无 total，按返回数量判断末页）。</summary>
    public async IAsyncEnumerable<CloudFile> ListAllAsync(string parentDirectoryId = "0",
        int pageSize = 50, string spaceType = "0",
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var page = 0; ; page++)
        {
            ct.ThrowIfCancellationRequested();
            var r = await ListAsync(parentDirectoryId, page, pageSize, spaceType: spaceType, ct: ct)
                .ConfigureAwait(false);
            var files = r?.Files ?? [];
            foreach (var f in files) yield return f;
            if (files.Count < pageSize) yield break;
        }
    }

    /// <summary>
    /// 分类文件列表（实测参数）：{clientId, spaceType, fileType, pageNum, pageSize, sortRule, isMergeSysFile}。
    /// fileType 必须是<b>数字字符串</b>：1=图片 2=视频 3=音频 4=文档 5=其他（传 photo/all/0 都返回空）。
    /// 返回 {files}。
    /// </summary>
    public Task<QueryAllFilesResult?> QueryTypeFilesAsync(string fileType, int pageNum = 0,
        int pageSize = 50, int sortRule = 0, string spaceType = "0", CancellationToken ct = default) =>
        _dispatcher.CallAsync<QueryAllFilesResult>("QueryTypeFiles", new
        {
            clientId = ClientId,
            spaceType,
            fileType,
            pageNum,
            pageSize,
            sortRule,
            isMergeSysFile = "1",
        }, ct: ct);

    /// <summary>上传断点日志（实测：空参数即可）→ DATA = {fileLogs, count}。</summary>
    public Task<UploadLogResult?> QueryUploadLogAsync(CancellationToken ct = default) =>
        _dispatcher.CallAsync<UploadLogResult>("QueryUploadLog", new { }, ct: ct);

    /// <summary>左侧目录树（返回 {directorys}）。家庭云实测 120002，需改用 ListAsync 取根目录。</summary>
    public Task<QueryDirectorysResult?> ListDirectoriesAsync(string spaceType = "0",
        string? familyId = null, CancellationToken ct = default)
    {
        var param = new Dictionary<string, object> { ["clientId"] = ClientId, ["spaceType"] = spaceType };
        if (familyId is not null) param["familyId"] = familyId;
        return _dispatcher.CallAsync<QueryDirectorysResult>("QueryDirectorys", param, ct: ct);
    }

    /// <summary>
    /// 全局搜索（实测参数）：{clientId, searchType, keyWord, pageNo, pageSize}。
    /// searchType：个人空间 "2"、家庭空间 "1"；pageNo 从 1 开始。
    /// 返回 DATA = {personalResult, familyResult}。
    /// </summary>
    public Task<SearchResult?> SearchAsync(string keyword, int pageNo = 1, int pageSize = 50,
        string spaceType = "0", CancellationToken ct = default) =>
        _dispatcher.CallAsync<SearchResult>("SearchFile", new
        {
            clientId = ClientId,
            searchType = spaceType == "0" ? "2" : "1",
            keyWord = keyword,
            pageNo,
            pageSize,
        }, ct: ct);

    /// <summary>新建目录，DATA 返回 {id}。家庭云必须带 familyId（实测缺失直接 500）。</summary>
    public Task<CreateDirectoryResult?> CreateDirectoryAsync(string directoryName, string parentDirectoryId,
        string spaceType = "0", string? familyId = null, CancellationToken ct = default)
    {
        var param = new Dictionary<string, object>
        {
            ["clientId"] = ClientId,
            ["directoryName"] = directoryName,
            ["parentDirectoryId"] = parentDirectoryId,
            ["spaceType"] = spaceType,
        };
        if (familyId is not null) param["familyId"] = familyId;
        return _dispatcher.CallAsync<CreateDirectoryResult>("CreateDirectory", param, ct: ct);
    }

    /// <summary>重命名文件或目录：{clientId, spaceType, type, fileType, id, name}。</summary>
    public Task RenameAsync(string id, string newName, int type, string fileType = "",
        string spaceType = "0", CancellationToken ct = default) =>
        _dispatcher.CallAsync<object>("RenameFileOrDirectory", new
        {
            clientId = ClientId,
            spaceType,
            type,
            fileType,
            id,
            name = newName,
        }, ct: ct);

    /// <summary>删除：目录走 dirList，文件走 fidList（均传 id 值）。家庭云需带 familyId。</summary>
    public Task DeleteAsync(IEnumerable<string>? dirIds = null, IEnumerable<string>? fileIds = null,
        string spaceType = "0", string? familyId = null, CancellationToken ct = default)
    {
        var param = new Dictionary<string, object>
        {
            ["clientId"] = ClientId,
            ["spaceType"] = spaceType,
            ["dirList"] = dirIds?.ToList() ?? [],
            ["fileList"] = fileIds?.ToList() ?? [],
        };
        if (familyId is not null) param["familyId"] = familyId;
        return _dispatcher.CallAsync<object>("DeleteFile", param, ct: ct);
    }

    /// <summary>
    /// 移动：{clientId, targetDirId, sourceType, targetType, dirList, fileList, secret:false}（实测通过）。
    /// 注意 <c>secret</c> 是参数内的字段，不影响传输加密。
    /// </summary>
    public Task MoveAsync(IEnumerable<string>? fileIds, IEnumerable<string>? dirIds, string targetDirId,
        string sourceType = "0", string targetType = "0", string? familyId = null, CancellationToken ct = default)
    {
        var param = new Dictionary<string, object>
        {
            ["clientId"] = ClientId,
            ["targetDirId"] = targetDirId,
            ["sourceType"] = sourceType,
            ["targetType"] = targetType,
            ["dirList"] = dirIds?.ToList() ?? [],
            ["fileList"] = fileIds?.ToList() ?? [],
            ["secret"] = false,
        };
        if (familyId is not null) param["familyId"] = familyId;
        return _dispatcher.CallAsync<object>("MoveFile", param, ct: ct);
    }

    /// <summary>复制：与移动同构（实测通过）。家庭云需带 familyId。</summary>
    public Task CopyAsync(IEnumerable<string>? fileIds, IEnumerable<string>? dirIds, string targetDirId,
        string sourceType = "0", string targetType = "0", string? familyId = null, CancellationToken ct = default)
    {
        var param = new Dictionary<string, object>
        {
            ["clientId"] = ClientId,
            ["targetDirId"] = targetDirId,
            ["sourceType"] = sourceType,
            ["targetType"] = targetType,
            ["dirList"] = dirIds?.ToList() ?? [],
            ["fileList"] = fileIds?.ToList() ?? [],
            ["secret"] = false,
        };
        if (familyId is not null) param["familyId"] = familyId;
        return _dispatcher.CallAsync<object>("CopyFile", param, ct: ct);
    }

    /// <summary>
    /// 批量获取下载直链（必须传 fid，不是 id）。目录可走 dirList 由服务端打包。
    /// spaceType=4（私密空间）需带 psToken，=1（家庭云）需带 familyId。
    /// </summary>
    public Task<List<DownloadUrlItem>?> GetDownloadUrlsAsync(List<string>? fids = null,
        List<string>? dirIds = null, string spaceType = "0",
        string? familyId = null, string? psToken = null, CancellationToken ct = default) =>
        _dispatcher.CallAsync<List<DownloadUrlItem>>("GetDownloadUrl",
            BuildDownloadParam(fids, dirIds, spaceType, familyId, psToken), ct: ct);

    private object BuildDownloadParam(List<string>? fids, List<string>? dirIds,
        string spaceType, string? familyId, string? psToken)
    {
        if (spaceType == "1")
            return new
            {
                clientId = ClientId,
                spaceType,
                familyId = familyId ?? "0",
                fidList = fids ?? [],
                dirList = dirIds ?? [],
            };
        if (spaceType == "4")
            return new
            {
                clientId = ClientId,
                spaceType,
                psToken = psToken ?? _session.PsToken ?? "",
                fidList = fids ?? [],
                dirList = dirIds ?? [],
            };
        return new
        {
            clientId = ClientId,
            spaceType,
            fidList = fids ?? [],
            dirList = dirIds ?? [],
        };
    }

    /// <summary>获取上传网关地址（GetZoneInfo；勿写死默认值）。</summary>
    public Task<ZoneInfo?> GetZoneInfoAsync(CancellationToken ct = default) =>
        _dispatcher.CallAsync<ZoneInfo>("GetZoneInfo", new
        {
            appId = WoCloudConstants.AppId,
            clientId = ClientId,
        }, ct: ct);
}

public sealed class ZoneInfo
{
    [System.Text.Json.Serialization.JsonPropertyName("url")] public string Url { get; set; } = "";
}

public sealed class CreateDirectoryResult
{
    [System.Text.Json.Serialization.JsonPropertyName("id")] public string Id { get; set; } = "";
}
