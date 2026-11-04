using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WoCloud.Client.Models;
using WoCloud.Client.Transfer;

namespace WoCloud.App.ViewModels;

/// <summary>飞传接收站（FlyPassFileList / DelFlyPass / ReceiveFlyPassBatch）。</summary>
public partial class FlyPassViewModel : ObservableObject
{
    private readonly FlyPassApi _flyPass;

    public ObservableCollection<FlyPassItem> Items { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private FlyPassItem? _selected;

    public FlyPassViewModel(FlyPassApi flyPass) => _flyPass = flyPass;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            Items.Clear();
            var r = await _flyPass.ListAsync(0, 100);
            foreach (var i in r?.List ?? []) Items.Add(i);
            StatusText = Items.Count == 0 ? "暂无飞传记录" : $"共 {Items.Count} 条";
        }
        catch (Exception e) { StatusText = "加载失败：" + e.Message; }
        finally { IsLoading = false; }
    }

    /// <summary>接收（转存到根目录）。</summary>
    public async Task ReceiveAsync(FlyPassItem? item)
    {
        if (item is null) return;
        try
        {
            await _flyPass.ReceiveAsync([item.FlyPassId], "0");
            StatusText = "接收成功，已保存到「全部文件」";
            await LoadAsync();
        }
        catch (Exception e) { StatusText = "接收失败：" + e.Message; }
    }

    /// <summary>删除飞传记录。</summary>
    public async Task DeleteAsync(FlyPassItem? item)
    {
        if (item is null) return;
        try
        {
            await _flyPass.DeleteAsync([item.FlyPassId]);
            StatusText = "已删除";
            await LoadAsync();
        }
        catch (Exception e) { StatusText = "删除失败：" + e.Message; }
    }
}
