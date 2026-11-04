using System.Collections.ObjectModel;
using System.ComponentModel;
using WoCloud.App.ViewModels;

namespace WoCloud.App.Services;

/// <summary>
/// 全局传输队列：直接复用文件页的 <see cref="FileListViewModel.TransferItems"/>，
/// 保证「传输」页与文件页看到的是同一份记录（不再复制集合，避免看不到任务）。
/// </summary>
public sealed class TransferQueue
{
    public ObservableCollection<TransferItem> Items { get; }

    public TransferQueue(FileListViewModel files)
    {
        Items = files.TransferItems;
        Items.CollectionChanged += (_, _) => Changed?.Invoke();
    }

    /// <summary>队列变化（新增/清空）时触发，供页面刷新统计。</summary>
    public event Action? Changed;

    public int RunningCount => Items.Count(i => i.State.Contains("中"));

    public void ClearCompleted()
    {
        foreach (var item in Items.Where(i =>
                     i.State.StartsWith("已完成") || i.State.StartsWith("失败")).ToList())
            Items.Remove(item);
        Changed?.Invoke();
    }

    public void ClearAll()
    {
        Items.Clear();
        Changed?.Invoke();
    }
}
