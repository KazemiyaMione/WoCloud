using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WoCloud.App.Services;
using WoCloud.App.ViewModels;
using WoCloud.Client.Models;

namespace WoCloud.App.Views;

public sealed partial class SharePage : Page
{
    public ShareViewModel Vm { get; } = App.Current.Services.GetRequiredService<ShareViewModel>();

    public SharePage()
    {
        InitializeComponent();
        _ = Vm.LoadAsync();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = Vm.LoadAsync();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ShareItem item) return;
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText($"我通过联通云盘分享文件给您\n链接：{item.Link}\n提取码：{item.ShareCode ?? "无"}");
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        Vm.StatusText = "已复制到剪贴板";
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ShareItem item) return;
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "取消分享",
            Content = $"确定取消「{item.ShareName}」的分享吗？",
            PrimaryButtonText = "取消分享",
            CloseButtonText = "返回",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            await Vm.CancelAsync(item);
    }

    /// <summary>打开他人分享：输入 shareId + 提取码。</summary>
    private async void OpenShare_Click(object sender, RoutedEventArgs e)
    {
        var idBox = new TextBox { Header = "分享 ID", PlaceholderText = "我的分享列表中的 shareId" };
        var codeBox = new TextBox { Header = "提取码", PlaceholderText = "如 Mm5K" };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(idBox);
        panel.Children.Add(codeBox);

        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "打开分享",
            Content = panel,
            PrimaryButtonText = "打开",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(idBox.Text)) return;
        await Vm.OpenShareAsync(idBox.Text.Trim(), codeBox.Text.Trim());
    }

    /// <summary>下载分享内的文件（分享文件用 fid 取直链）。</summary>
    private async void ShareFileDownload_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WoCloud.Client.Share.ShareFileEntry entry) return;
        if (string.IsNullOrEmpty(entry.Fid)) { Vm.StatusText = "该条目缺少 fid，无法下载"; return; }

        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        if (App.MainWin is not null)
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWin));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        try
        {
            var downloads = App.Current.Services.GetRequiredService<WoCloud.Client.Transfer.DownloadClient>();
            await downloads.DownloadAsync(entry.Fid, Path.Combine(folder.Path, entry.FileName));
            Vm.StatusText = "下载完成：" + entry.FileName;
        }
        catch (Exception ex) { Vm.StatusText = "下载失败：" + ex.Message; }
    }
}
