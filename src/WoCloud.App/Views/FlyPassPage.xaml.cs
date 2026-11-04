using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WoCloud.App.Services;
using WoCloud.App.ViewModels;

namespace WoCloud.App.Views;

public sealed partial class FlyPassPage : Page
{
    public FlyPassViewModel Vm { get; } = App.Current.Services.GetRequiredService<FlyPassViewModel>();

    public FlyPassPage()
    {
        InitializeComponent();
        _ = Vm.LoadAsync();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = Vm.LoadAsync();

    private async void Receive_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Selected is null) { Vm.StatusText = "请先选择一条飞传记录"; return; }
        await Vm.ReceiveAsync(Vm.Selected);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Selected is null) { Vm.StatusText = "请先选择一条飞传记录"; return; }
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "删除飞传",
            Content = $"确定删除「{Vm.Selected.FileName}」这条飞传记录吗？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            await Vm.DeleteAsync(Vm.Selected);
    }
}
