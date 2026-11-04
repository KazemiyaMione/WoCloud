using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WoCloud.App.Services;
using WoCloud.App.ViewModels;

namespace WoCloud.App.Views;

public sealed partial class PrivateSpacePage : Page
{
    public PrivateSpaceViewModel Vm { get; } = App.Current.Services.GetRequiredService<PrivateSpaceViewModel>();

    public PrivateSpacePage()
    {
        InitializeComponent();
        Preview.StatusChanged += msg => Vm.StatusText = msg;
    }

    /// <summary>单击文件预览（spaceType=4，控件内部会用会话里的 psToken）。</summary>
    private async void File_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WoCloud.Client.Models.CloudFile file) return;
        if (file.IsDirectory) { Vm.StatusText = "私密空间暂不支持子目录浏览"; return; }
        await Preview.ShowAsync(Vm.Files, file, "4");
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Selected is not { } file) return;
        await Preview.ShowAsync(Vm.Files, file, "4");
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e) => await Vm.UnlockAsync();

    /// <summary>PasswordBox.Password 不适合双向绑定，这里手动同步到 VM。</summary>
    /// <summary>缩略图加载失败时收起图片容器，露出底层的类型图标。</summary>
    private void Thumb_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image img && img.Parent is Border border) border.Visibility = Visibility.Collapsed;
    }

    private void Password_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box) Vm.Password = box.Password;
    }

    private async void Password_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is PasswordBox box) Vm.Password = box.Password;
        if (e.Key == Windows.System.VirtualKey.Enter) await Vm.UnlockAsync();
    }

    /// <summary>右键不会改变 ListView 选中项，必须手动同步，否则右键菜单操作的是旧选中项。</summary>
    private void Item_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WoCloud.Client.Models.CloudFile file) return;
        Vm.Selected = file;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = Vm.LoadAsync();

    private void Lock_Click(object sender, RoutedEventArgs e) => Vm.Lock();

    private async void MoveOut_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Selected is not { } file) return;
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "移出私密空间",
            Content = $"确定把「{file.Name}」移出到「全部文件」吗？",
            PrimaryButtonText = "移出",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            await Vm.MoveOutAsync(file);
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Selected is not { IsDirectory: false } file) return;
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        if (App.MainWin is not null)
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWin));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        await Vm.DownloadAsync(file, folder.Path);
    }
}
