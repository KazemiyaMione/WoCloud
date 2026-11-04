using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WoCloud.Client.Member;
using WoCloud.Client.Models;
using WoCloud.Client.Share;
using WoCloud.Client.Transfer;

namespace WoCloud.App.ViewModels;

/// <summary>分享管理页。</summary>
public partial class ShareViewModel : ObservableObject
{
    private readonly ShareApi _shares;
    public ObservableCollection<ShareItem> Shares { get; } = [];
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "";

    public ShareViewModel(ShareApi shares) => _shares = shares;

    [ObservableProperty] private int _total;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            Shares.Clear();
            var r = await _shares.ListMySharesAsync();
            foreach (var s in r?.List ?? [])
                Shares.Add(s);
            Total = r?.Total ?? Shares.Count;
            StatusText = $"共 {Total} 条分享";
        }
        catch (Exception e) { StatusText = "加载失败：" + e.Message; }
        finally { IsLoading = false; }
    }

    /// <summary>取消分享（接口参数含 clientId + 逗号串 shareId，实测通过）。</summary>
    public async Task CancelAsync(ShareItem? item)
    {
        if (item?.ShareId is null) return;
        try
        {
            await _shares.CancelShareAsync([item.ShareId]);
            await LoadAsync();
        }
        catch (Exception e) { StatusText = "取消分享失败：" + e.Message; }
    }

    // ---------- 查看他人分享 ----------

    /// <summary>当前打开的分享的文件列表。</summary>
    public ObservableCollection<ShareFileEntry> ShareFiles { get; } = [];

    [ObservableProperty] private ShareOwnerInfo? _owner;
    [ObservableProperty] private string _shareTitle = "";

    public string OwnerText => Owner is null ? ""
        : $"{Owner.NickName}（LV{Owner.VipLevel}） · {Owner.StateText} · 有效期至 {Owner.ExpireText}";

    partial void OnOwnerChanged(ShareOwnerInfo? value) => OnPropertyChanged(nameof(OwnerText));

    /// <summary>打开分享：先查分享信息，再用提取码拉文件列表。</summary>
    public async Task OpenShareAsync(string shareId, string shareCode)
    {
        try
        {
            ShareFiles.Clear();
            ShareTitle = "分享 " + shareId;
            Owner = await _shares.GetShareInfoAsync(shareId);
            var files = await _shares.GetShareFilesAsync(shareId, shareCode);
            foreach (var f in files ?? []) ShareFiles.Add(f);
            StatusText = $"分享内容 {ShareFiles.Count} 项";
        }
        catch (Exception e)
        {
            StatusText = "打开分享失败：" + e.Message;
        }
    }
}

/// <summary>传输队列页（与文件页共享同一集合，实时可见）。</summary>
public partial class TransferViewModel : ObservableObject
{
    private readonly WoCloud.App.Services.TransferQueue _queue;

    public ObservableCollection<TransferItem> Items => _queue.Items;

    [ObservableProperty] private string _statusText = "暂无传输任务";

    public TransferViewModel(WoCloud.App.Services.TransferQueue queue)
    {
        _queue = queue;
        _queue.Changed += Refresh;
        Refresh();
    }

    public void Refresh()
    {
        StatusText = Items.Count == 0
            ? "暂无传输任务"
            : $"共 {Items.Count} 项 · 进行中 {Items.Count(i => i.State.Contains('中'))} · 失败 {Items.Count(i => i.IsFailed)}";
        OnPropertyChanged(nameof(Items));
    }

    /// <summary>清理已结束的记录（完成/失败）。</summary>
    public void ClearFinished()
    {
        _queue.ClearCompleted();
        Refresh();
    }

    public void ClearAll()
    {
        _queue.ClearAll();
        Refresh();
    }
}
