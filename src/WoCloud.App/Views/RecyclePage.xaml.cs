using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WoCloud.App.Services;
using WoCloud.App.ViewModels;

namespace WoCloud.App.Views;

public sealed partial class RecyclePage : Page
{
    public RecycleViewModel Vm { get; } = App.Current.Services.GetRequiredService<RecycleViewModel>();

    public RecyclePage()
    {
        InitializeComponent();
        _ = Vm.LoadAsync();
    }

    private async void Reduce_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Selected is null) { Vm.StatusText = "请先选择一项"; return; }
        await Vm.ReduceAsync(Vm.Selected);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Selected is null) { Vm.StatusText = "请先选择一项"; return; }
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "永久删除",
            Content = $"确定永久删除「{Vm.Selected.Name}」吗？此操作不可恢复。",
            PrimaryButtonText = "永久删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            await Vm.DeleteAsync(Vm.Selected);
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "清空回收站",
            Content = "回收站内所有文件将被彻底删除，无法恢复，确认继续吗？",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            await Vm.ClearAsync();
    }
}
