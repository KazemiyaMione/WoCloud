using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WoCloud.Client.Core;
using WoCloud.Client.Files;
using WoCloud.Client.Models;
using WoCloud.Client.Space;
using WoCloud.Client.Transfer;

namespace WoCloud.App.ViewModels;

/// <summary>私密空间：解锁（PrivateSpaceLogin）→ 浏览（spaceType=4 + psToken）→ 移出 / 下载。</summary>
public partial class PrivateSpaceViewModel : ObservableObject
{
    private readonly PrivateSpaceApi _ps;
    private readonly FileApi _files;
    private readonly DownloadClient _downloads;
    private readonly SessionState _session;

    public ObservableCollection<CloudFile> Files { get; } = [];

    [ObservableProperty] private bool _isUnlocked;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _statusText = "请输入私密空间密码解锁";
    [ObservableProperty] private CloudFile? _selected;

    /// <summary>供按钮 IsEnabled 绑定。</summary>
    public bool IsNotLoading => !IsLoading;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsNotLoading));

    /// <summary>密码校验次数被服务端限制（PrivateSpaceLogin 返回 isPass=0 + 该描述）。</summary>
    [ObservableProperty] private bool _isLockedOut;

    public PrivateSpaceViewModel(PrivateSpaceApi ps, FileApi files, DownloadClient downloads,
        SessionState session)
    {
        _ps = ps;
        _files = files;
        _downloads = downloads;
        _session = session;
        IsUnlocked = ps.IsUnlocked;
        if (IsUnlocked) _ = LoadAsync();
    }

    public string SpaceIdText => _session.PsToken is { Length: > 6 } t ? "已解锁 · " + t[..6] + "…" : "";

    /// <summary>解锁。</summary>
    public async Task UnlockAsync()
    {
        if (string.IsNullOrWhiteSpace(Password))
        {
            StatusText = "请输入私密空间密码";
            return;
        }

        IsLoading = true;
        IsLockedOut = false;
        StatusText = "正在校验密码…";
        try
        {
            var r = await _ps.LoginAsync(Password);
            if (r is { IsPass: "1" })
            {
                IsUnlocked = true;
                IsLockedOut = false;
                Password = "";
                StatusText = "解锁成功";
                OnPropertyChanged(nameof(SpaceIdText));
                await LoadAsync();
                return;
            }

            // 服务端限制：isPass="0" + desc="密码校验次数超过限制"
            var desc = r?.Desc ?? "密码不正确";
            IsLockedOut = desc.Contains("次数") || desc.Contains("限制");
            StatusText = IsLockedOut
                ? "密码错误次数过多，服务端已临时锁定。请等待解锁冷却，或到官方网页「私密空间 → 找回密码」重置后再试。"
                : "解锁失败：" + desc;
        }
        catch (WoCloudException e) when (e.Code is "110001" or "1001" or "130007" or "130008")
        {
            _session.SetPsToken("");
            IsUnlocked = false;
            StatusText = "登录状态已失效，请重新登录后再解锁";
        }
        catch (Exception e)
        {
            StatusText = "解锁失败：" + e.Message;
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (!IsUnlocked) return;
        IsLoading = true;
        try
        {
            Files.Clear();
            var r = await _files.ListAsync("0", 0, 100, spaceType: "4");
            foreach (var f in r?.Files ?? []) Files.Add(f);
            StatusText = Files.Count == 0 ? "私密空间为空" : $"共 {Files.Count} 项";
        }
        catch (WoCloudException e) when (e.Code is "110001" or "1000")
        {
            // psToken 过期/无效：退回解锁界面，而不是停在“已解锁但列表空”
            _session.SetPsToken("");
            IsUnlocked = false;
            Files.Clear();
            StatusText = "私密空间令牌已失效，请重新输入密码解锁";
            OnPropertyChanged(nameof(SpaceIdText));
        }
        catch (Exception e) { StatusText = "加载失败：" + e.Message; }
        finally { IsLoading = false; }
    }

    /// <summary>移出到个人云根目录。</summary>
    public async Task MoveOutAsync(CloudFile? file)
    {
        if (file is null) return;
        try
        {
            if (file.IsDirectory) await _ps.MoveOutAsync(null, [file.Id]);
            else await _ps.MoveOutAsync([file.Id], null);
            StatusText = "已移出到「全部文件」";
            await LoadAsync();
        }
        catch (Exception e) { StatusText = "移出失败：" + e.Message; }
    }

    /// <summary>移入（供其他页面调用前先解锁）。</summary>
    public async Task MoveInAsync(IEnumerable<string> fileIds, IEnumerable<string>? dirIds = null)
    {
        try
        {
            await _ps.MoveInAsync(fileIds.ToList(), dirIds?.ToList());
            StatusText = "已移入私密空间";
        }
        catch (Exception e) { StatusText = "移入失败：" + e.Message; }
    }

    public async Task DownloadAsync(CloudFile file, string folder)
    {
        try
        {
            await _downloads.DownloadAsync(file.Fid, Path.Combine(folder, file.Name), spaceType: "4");
            StatusText = "下载完成：" + file.Name;
        }
        catch (Exception e) { StatusText = "下载失败：" + e.Message; }
    }

    /// <summary>锁屏（清除本地 psToken）。</summary>
    public void Lock()
    {
        _session.SetPsToken("");
        IsUnlocked = false;
        Files.Clear();
        StatusText = "已锁定";
        OnPropertyChanged(nameof(SpaceIdText));
    }
}
