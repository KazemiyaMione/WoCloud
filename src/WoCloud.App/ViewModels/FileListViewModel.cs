using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WoCloud.Client.Files;
using WoCloud.Client.Models;
using WoCloud.Client.Share;
using WoCloud.Client.Transfer;

namespace WoCloud.App.ViewModels;

/// <summary>面包屑节点。</summary>
public record CrumbNode(string Id, string Name);

/// <summary>空间选项（个人云 / 家庭云群组）。</summary>
public sealed record SpaceOption(string Name, string SpaceType, string? FamilyId)
{
    public override string ToString() => Name;
}

/// <summary>传输队列条目。</summary>
public partial class TransferItem : ObservableObject
{
    public TransferItem(string name, string kind, long total)
    {
        Name = name; Kind = kind; Total = total;
    }
    public string Name { get; }
    public string Kind { get; }          // 下载 / 上传
    public long Total { get; }
    [ObservableProperty] private long _sent;
    [ObservableProperty] private string _state = "排队中";
    public double Progress => Total > 0 ? (double)Sent / Total : 0;
    public string ProgressText => $"{Sent * 100 / Math.Max(Total, 1)}%";
    public string SizeText => $"{CloudFile.FormatSize(Sent)} / {CloudFile.FormatSize(Total)}";
    public string TimeText { get; } = DateTime.Now.ToString("HH:mm:ss");
    public bool IsFailed => State.StartsWith("失败");

    partial void OnSentChanged(long value)
    {
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(SizeText));
    }

    partial void OnStateChanged(string value) => OnPropertyChanged(nameof(IsFailed));
}

public partial class FileListViewModel : ObservableObject
{
    private readonly FileApi _files;
    private readonly DownloadClient _downloads;
    private readonly UploadClient _uploads;
    private readonly ShareApi _shares;
    private readonly WoCloud.Client.Misc.MiscApi _misc;

    private const int PageSize = 100;

    public ObservableCollection<CloudFile> Files { get; } = [];
    public ObservableCollection<CrumbNode> Breadcrumbs { get; } = [new("0", "全部文件")];
    public ObservableCollection<TransferItem> TransferItems { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _searchKeyword = "";
    [ObservableProperty] private CloudFile? _selectedFile;
    [ObservableProperty] private string _statusText = "";

    private string _currentDir = "0";

    /// <summary>可选空间（个人云 / 家庭云 / 校园云）。私密空间不在此切换，走左侧独立页。</summary>
    public ObservableCollection<SpaceOption> Spaces { get; } =
    [
        new("个人云", "0", null),
        new("校园云", "3", null),
    ];

    [ObservableProperty] private SpaceOption? _selectedSpace;

    private readonly WoCloud.Client.Core.SessionState _session;

    public FileListViewModel(FileApi files, DownloadClient downloads, UploadClient uploads,
        ShareApi shares, WoCloud.Client.Misc.MiscApi misc,
        WoCloud.Client.Core.SessionState session)
    {
        _files = files;
        _downloads = downloads;
        _uploads = uploads;
        _shares = shares;
        _misc = misc;
        _session = session;
    }

    /// <summary>加载家庭云群组到空间下拉框（插在校园云之前）。</summary>
    public async Task LoadSpacesAsync()
    {
        try
        {
            var groups = await _misc.GetFamilyGroupsAsync();
            var insertAt = Spaces.IndexOf(Spaces.First(s => s.SpaceType == "3"));
            foreach (var g in groups?.All ?? [])
                if (!Spaces.Any(s => s.FamilyId == g.IdText))
                {
                    Spaces.Insert(insertAt, new SpaceOption("家庭云 · " + (g.GroupName ?? g.IdText), "1", g.IdText));
                    insertAt++;
                }
        }
        catch { /* 家庭云不可用时静默降级 */ }
    }

    /// <summary>切换空间（个人云 / 家庭云 / 校园云）。</summary>
    public async Task SwitchSpaceAsync(SpaceOption? space)
    {
        if (space is null) return;
        SelectedSpace = space;
        _currentDir = "0";
        Category = "all";
        Breadcrumbs.Clear();
        Breadcrumbs.Add(new CrumbNode("0", space.Name));
        await LoadAsync();
    }

    public CrumbNode? CurrentCrumb => Breadcrumbs.LastOrDefault();

    [ObservableProperty] private int _pageNum;
    [ObservableProperty] private bool _hasMore;
    /// <summary>分类：all / photo / video / music / file / other（对应 QueryTypeFiles.fileType）。</summary>
    [ObservableProperty] private string _category = "all";

    /// <summary>切换分类（all 走 QueryAllFiles，其余走 QueryTypeFiles）。</summary>
    public async Task SwitchCategoryAsync(string category)
    {
        Category = category;
        if (category == "all")
        {
            Breadcrumbs.Clear();
            Breadcrumbs.Add(new CrumbNode("0", "全部文件"));
            _currentDir = "0";
        }
        await LoadAsync();
    }

    [RelayCommand]
    public async Task LoadAsync(bool force = false)
    {
        if (IsLoading) return;
        IsLoading = true;
        PageNum = 0;
        try
        {
            Files.Clear();
            var r = await FetchPageAsync(0);
            foreach (var f in r ?? []) Files.Add(f);
            HasMore = (r?.Count ?? 0) >= PageSize;
            StatusText = $"{CategoryText}：共 {Files.Count} 项";
        }
        catch (Exception e) { StatusText = "加载失败：" + e.Message; }
        finally { IsLoading = false; }
    }

    private string CategoryText => Category switch
    {
        "photo" => "照片",
        "video" => "视频",
        "music" => "音乐",
        "file" => "文档",
        "other" => "其他",
        _ => "全部文件",
    };

    /// <summary>按当前分类/目录取一页数据。</summary>
    private async Task<List<CloudFile>?> FetchPageAsync(int pageNum)
    {
        var spaceType = SelectedSpace?.SpaceType ?? "0";
        // 分类视图只在个人云下有意义
        if (Category != "all" && spaceType == "0")
        {
            var t = await _files.QueryTypeFilesAsync(ToApiFileType(Category), pageNum, PageSize);
            return t?.Files;
        }
        var r = await _files.ListAsync(_currentDir, pageNum, PageSize,
            spaceType: spaceType, familyId: SelectedSpace?.FamilyId);
        return r?.Files;
    }

    /// <summary>分类 → 接口 fileType（实测：1图片 2视频 3音频 4文档 5其他）。</summary>
    private static string ToApiFileType(string category) => category switch
    {
        "photo" => "1",
        "video" => "2",
        "music" => "3",
        "file" => "4",
        "other" => "5",
        _ => "1",
    };

    /// <summary>加载下一页并追加。</summary>
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsLoading || !HasMore) return;
        IsLoading = true;
        try
        {
            var list = await FetchPageAsync(++PageNum) ?? [];
            foreach (var f in list) Files.Add(f);
            HasMore = list.Count >= PageSize;
            StatusText = $"已加载 {Files.Count} 项";
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task OpenAsync(CloudFile? file)
    {
        if (file is null) return;
        if (file.IsDirectory)
        {
            // 从分类视图点进目录时，回到目录浏览模式
            if (Category != "all")
            {
                Category = "all";
                Breadcrumbs.Clear();
                Breadcrumbs.Add(new CrumbNode("0", "全部文件"));
            }
            _currentDir = file.Id;
            Breadcrumbs.Add(new CrumbNode(file.Id, file.Name));
            await LoadAsync();
        }
    }

    [RelayCommand]
    public async Task GoCrumbAsync(CrumbNode? crumb)
    {
        if (crumb is null) return;
        while (Breadcrumbs.Count > 1 && Breadcrumbs[^1].Id != crumb.Id)
            Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);
        _currentDir = crumb.Id;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword)) { await LoadAsync(); return; }
        IsLoading = true;
        try
        {
            Files.Clear();
            var r = await _files.SearchAsync(SearchKeyword.Trim());
            foreach (var f in r?.Files ?? []) Files.Add(f);
            StatusText = $"搜索到 {Files.Count} 项";
        }
        catch (Exception e) { StatusText = "搜索失败：" + e.Message; }
        finally { IsLoading = false; }
    }

    /// <summary>当前空间类型（0 个人云 / 1 家庭云 / 3 校园云）。</summary>
    private string CurrentSpaceType => SelectedSpace?.SpaceType ?? "0";

    /// <summary>家庭云群组 id（仅家庭云视图非空）。家庭云所有接口缺它直接 500。</summary>
    private string? CurrentFamilyId => CurrentSpaceType == "1" ? SelectedSpace?.FamilyId : null;

    [RelayCommand]
    private async Task NewFolderAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            await _files.CreateDirectoryAsync(name.Trim(), _currentDir, CurrentSpaceType, CurrentFamilyId);
            await LoadAsync();
        }
        catch (Exception e) { StatusText = e.Message; }
    }

    /// <summary>重命名（参数：type/fileType/id/name，经实测）。</summary>
    public async Task RenameAsync(CloudFile file, string newName)
    {
        try
        {
            await _files.RenameAsync(file.Id, newName, file.Type, file.FileType, CurrentSpaceType);
            await LoadAsync();
        }
        catch (Exception e) { StatusText = "重命名失败：" + e.Message; }
    }

    /// <summary>删除（目录走 dirList，文件走 fidList，值均为 id）。</summary>
    public async Task DeleteAsync(CloudFile file)
    {
        try
        {
            if (file.IsDirectory)
                await _files.DeleteAsync(dirIds: [file.Id], spaceType: CurrentSpaceType, familyId: CurrentFamilyId);
            else
                await _files.DeleteAsync(fileIds: [file.Id], spaceType: CurrentSpaceType, familyId: CurrentFamilyId);
            await LoadAsync();
        }
        catch (Exception e) { StatusText = "删除失败：" + e.Message; }
    }

    /// <summary>下载到指定目录（UI 层用 FolderPicker 选择）。</summary>
    public async Task DownloadAsync(CloudFile file, string targetDir)
    {
        var target = Path.Combine(targetDir, file.Name);
        var item = new TransferItem(file.Name, "下载", file.Size) { State = "下载中" };
        TransferItems.Insert(0, item);
        var progress = new Progress<double>(p =>
        {
            item.Sent = (long)(file.Size * p);
            item.State = $"下载中 {item.ProgressText}";
        });
        try
        {
            await _downloads.DownloadAsync(file.Fid, target, progress,
                spaceType: CurrentSpaceType, familyId: SelectedSpace?.FamilyId);
            item.Sent = file.Size;
            item.State = "已完成";
            LastTransferMessage = $"下载完成：{file.Name} → {target}";
        }
        catch (Exception e)
        {
            item.State = "失败：" + e.Message;
            LastTransferMessage = $"下载失败：{e.Message}";
        }
    }

    /// <summary>最近一次传输的结果文案（页面显示在 InfoBar）。</summary>
    public string LastTransferMessage { get; private set; } = "";

    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dq =
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

    /// <summary>上传本地文件到当前目录。服务端限速按上传任务计，这里并行传多个文件以叠加吞吐。</summary>
    public async Task UploadAsync(IEnumerable<string> localPaths)
    {
        var jobs = new List<(TransferItem Item, string Path, string Name, long Length, Progress<double> Progress)>();
        foreach (var path in localPaths)
        {
            var name = Path.GetFileName(path);
            var fi = new FileInfo(path);
            var item = new TransferItem(name, "上传", fi.Length) { State = "上传中" };
            TransferItems.Insert(0, item);
            // Progress 必须在 UI 线程构造，回调才会被派发回 UI 线程
            var progress = new Progress<double>(p =>
            {
                item.Sent = (long)(fi.Length * p);
                item.State = $"上传中 {item.ProgressText}";
            });
            jobs.Add((item, path, name, fi.Length, progress));
        }
        if (jobs.Count == 0) return;

        var parallel = Math.Clamp(_uploads.FileParallelism, 1, jobs.Count);
        using var gate = new SemaphoreSlim(parallel);
        var psToken = CurrentSpaceType == "4" ? _session.PsToken : null;
        var spaceType = CurrentSpaceType;
        var familyId = CurrentFamilyId;
        var targetDir = _currentDir;

        var tasks = jobs.Select(j => Task.Run(async () =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await _uploads.UploadAsync(j.Path, targetDir, progress: j.Progress,
                    psToken: psToken, spaceType: spaceType, familyId: familyId).ConfigureAwait(false);
                _dq.TryEnqueue(() =>
                {
                    j.Item.State = "已完成";
                    LastTransferMessage = $"上传完成：{j.Name}";
                    StatusText = LastTransferMessage;
                });
            }
            catch (Exception e)
            {
                // 服务端业务码（如 130004 文件夹不存在）会原样带到界面，不再假装成功
                _dq.TryEnqueue(() =>
                {
                    j.Item.State = "失败：" + e.Message;
                    LastTransferMessage = $"上传失败：{j.Name} — {e.Message}";
                    StatusText = LastTransferMessage;
                });
            }
            finally { gate.Release(); }
        })).ToArray();

        // 不用 ConfigureAwait(false)：后续 LoadAsync 触发 UI 绑定，必须回到 UI 线程
        await Task.WhenAll(tasks);
        await LoadAsync();
    }

    /// <summary>上传整个本地文件夹：先在云端建同名目录，再递归建子目录并上传文件。</summary>
    public async Task UploadFolderAsync(string localFolder)
    {
        var root = new DirectoryInfo(localFolder);
        if (!root.Exists) { StatusText = "文件夹不存在：" + localFolder; return; }

        StatusText = $"正在创建目录：{root.Name}";
        try
        {
            var created = await _files.CreateDirectoryAsync(root.Name, _currentDir, CurrentSpaceType, CurrentFamilyId);
            if (string.IsNullOrEmpty(created?.Id))
            {
                StatusText = "创建目录失败：" + root.Name;
                return;
            }

            var (ok, fail) = await UploadFolderCoreAsync(root, created.Id);
            LastTransferMessage = fail == 0
                ? $"文件夹上传完成：{root.Name}（{ok} 个文件）"
                : $"文件夹上传结束：成功 {ok} 个，失败 {fail} 个";
            StatusText = LastTransferMessage;
        }
        catch (Exception e)
        {
            StatusText = "文件夹上传失败：" + e.Message;
        }
        await LoadAsync();
    }

    /// <summary>递归：先建子目录，再并行上传当前层文件。</summary>
    private async Task<(int Ok, int Fail)> UploadFolderCoreAsync(DirectoryInfo dir, string parentId)
    {
        int ok = 0, fail = 0;
        foreach (var sub in dir.GetDirectories())
        {
            try
            {
                var child = await _files.CreateDirectoryAsync(sub.Name, parentId, CurrentSpaceType, CurrentFamilyId);
                if (string.IsNullOrEmpty(child?.Id)) { fail++; continue; }
                var r = await UploadFolderCoreAsync(sub, child.Id);
                ok += r.Ok;
                fail += r.Fail;
            }
            catch { fail++; }
        }

        var files = dir.GetFiles();
        var jobs = new List<(TransferItem Item, string Path, long Length, Progress<double> Progress)>();
        foreach (var file in files)
        {
            var item = new TransferItem(file.Name, "上传", file.Length) { State = "上传中" };
            TransferItems.Insert(0, item);
            var progress = new Progress<double>(p =>
            {
                item.Sent = (long)(file.Length * p);
                item.State = $"上传中 {item.ProgressText}";
            });
            jobs.Add((item, file.FullName, file.Length, progress));
        }
        if (jobs.Count == 0) return (ok, fail);

        var parallel = Math.Clamp(_uploads.FileParallelism, 1, jobs.Count);
        using var gate = new SemaphoreSlim(parallel);
        var psToken = CurrentSpaceType == "4" ? _session.PsToken : null;
        var spaceType = CurrentSpaceType;
        var familyId = CurrentFamilyId;

        var tasks = jobs.Select(j => Task.Run(async () =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await _uploads.UploadAsync(j.Path, parentId, progress: j.Progress,
                    psToken: psToken, spaceType: spaceType, familyId: familyId).ConfigureAwait(false);
                _dq.TryEnqueue(() => j.Item.State = "已完成");
                Interlocked.Increment(ref ok);
            }
            catch (Exception e)
            {
                _dq.TryEnqueue(() => j.Item.State = "失败：" + e.Message);
                Interlocked.Increment(ref fail);
            }
            finally { gate.Release(); }
        })).ToArray();

        // 同上：保持 UI 上下文，避免后续 StatusText/列表更新跨线程
        await Task.WhenAll(tasks);
        return (ok, fail);
    }

    /// <summary>可选的移动/复制目标目录（根目录 + 一级目录；随当前空间走）。
    /// 家庭云的 QueryDirectorys 返回 120002（实测），改用列根目录取子目录。</summary>
    public async Task<List<CloudDirectory>> GetDirectoryChoicesAsync()
    {
        try
        {
            if (CurrentSpaceType == "1")
            {
                var r = await _files.ListAsync("0", spaceType: "1", familyId: SelectedSpace?.FamilyId);
                return (r?.Files ?? []).Where(f => f.IsDirectory).Select(f => new CloudDirectory
                {
                    Id = f.Id,
                    DirectoryName = f.Name,
                }).ToList();
            }
            var d = await _files.ListDirectoriesAsync(CurrentSpaceType);
            return d?.Directorys ?? [];
        }
        catch (Exception e)
        {
            StatusText = "读取目录失败：" + e.Message;
            return [];
        }
    }

    /// <summary>移动或复制到目标目录（实测参数：fileList/dirList + sourceType/targetType）。</summary>
    public async Task MoveOrCopyAsync(CloudFile file, string targetDirId, bool copy)
    {
        try
        {
            // 同空间内移动/复制；跨空间（如私密→个人）请用「移出私密空间」
            var st = CurrentSpaceType;
            var fid = CurrentFamilyId;
            if (copy)
            {
                if (file.IsDirectory) await _files.CopyAsync(null, [file.Id], targetDirId, st, st, fid);
                else await _files.CopyAsync([file.Id], null, targetDirId, st, st, fid);
            }
            else
            {
                if (file.IsDirectory) await _files.MoveAsync(null, [file.Id], targetDirId, st, st, fid);
                else await _files.MoveAsync([file.Id], null, targetDirId, st, st, fid);
            }
            await LoadAsync();
            StatusText = copy ? "复制成功" : "移动成功";
        }
        catch (Exception e)
        {
            StatusText = (copy ? "复制失败：" : "移动失败：") + e.Message;
        }
    }

    /// <summary>创建分享（默认 7 天、系统提取码；fileIds 为文件 id 逗号串）。</summary>
    public async Task<ShareItem?> ShareAsync(CloudFile file)
    {
        try
        {
            return file.IsDirectory
                ? await _shares.CreateShareAsync([], [file.Id])
                : await _shares.CreateShareAsync([file.Id]);
        }
        catch (Exception e)
        {
            StatusText = "分享失败：" + e.Message;
            return null;
        }
    }
}
