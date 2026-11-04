using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WoCloud.Client.Files;
using WoCloud.Client.Models;

namespace WoCloud.App.ViewModels;

/// <summary>回收站（QueryRecycleData / ReductionRecycleData / DeleteRecycleData / EmptyRecycleData）。</summary>
public partial class RecycleViewModel : ObservableObject
{
    private readonly RecycleApi _recycle;

    public ObservableCollection<RecycleItem> Items { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private RecycleItem? _selected;

    public RecycleViewModel(RecycleApi recycle) => _recycle = recycle;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            Items.Clear();
            var list = await _recycle.ListAsync(1, 100);
            foreach (var i in list ?? []) Items.Add(i);
            StatusText = Items.Count == 0 ? "回收站是空的" : $"共 {Items.Count} 项";
        }
        catch (Exception e) { StatusText = "加载失败：" + e.Message; }
        finally { IsLoading = false; }
    }

    /// <summary>还原到原位置。</summary>
    public async Task ReduceAsync(RecycleItem? item)
    {
        if (item is null) return;
        try
        {
            await _recycle.ReduceAsync([item.DeleteNo]);
            StatusText = "已还原";
            await LoadAsync();
        }
        catch (Exception e) { StatusText = "还原失败：" + e.Message; }
    }

    /// <summary>永久删除。</summary>
    public async Task DeleteAsync(RecycleItem? item)
    {
        if (item is null) return;
        try
        {
            await _recycle.DeleteAsync([item.DeleteNo]);
            StatusText = "已永久删除";
            await LoadAsync();
        }
        catch (Exception e) { StatusText = "删除失败：" + e.Message; }
    }

    /// <summary>清空回收站。</summary>
    public async Task ClearAsync()
    {
        try
        {
            await _recycle.ClearAsync();
            StatusText = "回收站已清空";
            await LoadAsync();
        }
        catch (Exception e) { StatusText = "清空失败：" + e.Message; }
    }
}
