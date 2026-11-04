using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using WoCloud.App.Services;
using WoCloud.App.ViewModels;
using WoCloud.Client.Models;
using Windows.Storage.Pickers;

namespace WoCloud.App.Views;

public sealed partial class FileListPage : Page, System.ComponentModel.INotifyPropertyChanged
{
    public FileListViewModel Vm { get; } = App.Current.Services.GetRequiredService<FileListViewModel>();

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public FileListPage()
    {
        InitializeComponent();
        Vm.PropertyChanged += (_, _) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(string.Empty));
        Preview.StatusChanged += msg => Vm.StatusText = msg;
        if (Vm.SelectedSpace is null) Vm.SelectedSpace = Vm.Spaces[0];
        _ = Vm.LoadAsync();
        _ = Vm.LoadSpacesAsync(); // 家庭云群组（失败静默）
    }

    private async void Space_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as ComboBox)?.SelectedItem is SpaceOption space && space != Vm.SelectedSpace)
            await Vm.SwitchSpaceAsync(space);
    }

    public string NewFolderName { get; set; } = "新建文件夹";

    public bool HasStatus => !string.IsNullOrEmpty(Vm.StatusText);

    private async void Crumb_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CrumbNode node)
            await Vm.GoCrumbAsync(node);
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var box = new TextBox { Text = "新建文件夹" };
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "新建文件夹",
            Content = box,
            PrimaryButtonText = "创建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
            await Vm.NewFolderCommand.ExecuteAsync(box.Text.Trim());
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = Vm.LoadAsync();

    private void LoadMore_Click(object sender, RoutedEventArgs e) => _ = Vm.LoadMoreCommand.ExecuteAsync(null);

    private void Category_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is SelectorBarItem { Tag: string tag } && tag != Vm.Category)
            _ = Vm.SwitchCategoryAsync(tag);
    }

    private void Search_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
            _ = Vm.SearchCommand.ExecuteAsync(null);
    }

    private async void File_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not CloudFile f) return;
        if (f.IsDirectory) await Vm.OpenAsync(f);
        else await Preview.ShowAsync(Vm.Files, f, Vm.SelectedSpace?.SpaceType ?? "0", Vm.SelectedSpace?.FamilyId);
    }

    private void File_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    // ---------------- 预览 ----------------

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedFile is not { } file) return;
        if (file.IsDirectory) await Vm.OpenAsync(file);
        else await Preview.ShowAsync(Vm.Files, file, Vm.SelectedSpace?.SpaceType ?? "0", Vm.SelectedSpace?.FamilyId);
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedFile is not { } file || file.IsDirectory) return;
        var folder = await PickFolderAsync();
        if (folder is null) return;
        Vm.StatusText = "开始下载：" + file.Name;
        await Vm.DownloadAsync(file, folder.Path);
        Vm.StatusText = Vm.LastTransferMessage;
    }

    /// <summary>下载到设置页里配置的默认目录。</summary>
    private async void DownloadDefault_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedFile is not { } file || file.IsDirectory) return;
        var dir = App.Current.Services.GetRequiredService<WoCloud.App.Services.AppSettings>().DownloadFolder;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            Vm.StatusText = "默认下载目录不可用，请到「设置」里重新选择";
            return;
        }
        await Vm.DownloadAsync(file, dir);
        Vm.StatusText = Vm.LastTransferMessage;
    }

    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedFile is not { } file || file.IsDirectory) return;
        var share = await Vm.ShareAsync(file);
        if (share is null) return;
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "分享已创建",
            Content = $"链接：{share.ShareUrl ?? "-"}\n提取码：{share.ShareCode ?? "无"}",
            CloseButtonText = "复制并关闭",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.None && (share.Link ?? share.ShareUrl) is { Length: > 0 } link)
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText($"{link} 提取码:{share.ShareCode ?? "无"}");
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        }
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedFile is not { } file) return;
        var box = new TextBox { Text = file.Name };
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "重命名", Content = box,
            PrimaryButtonText = "确定", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary && box.Text != file.Name && box.Text.Length > 0)
            await Vm.RenameAsync(file, box.Text);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedFile is not { } file) return;
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "删除",
            Content = $"确定删除「{file.Name}」吗？（可在网页端回收站恢复）",
            PrimaryButtonText = "删除", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            await Vm.DeleteAsync(file);
    }

    /// <summary>移入私密空间（需先解锁；未解锁则提示去私密空间页解锁）。</summary>
    private async void MoveToPrivate_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedFile is not { } file) return;
        var ps = App.Current.Services.GetRequiredService<PrivateSpaceViewModel>();
        if (!ps.IsUnlocked)
        {
            Vm.StatusText = "请先在左侧「私密空间」页用密码解锁";
            return;
        }
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "移入私密空间",
            Content = $"确定把「{file.Name}」移入私密空间吗？移入后仅能用密码访问。",
            PrimaryButtonText = "移入",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        if (file.IsDirectory)
            await ps.MoveInAsync([], [file.Id]);
        else
            await ps.MoveInAsync([file.Id]);
        Vm.StatusText = ps.StatusText;
        await Vm.LoadAsync();
    }

    private async void Move_Click(object sender, RoutedEventArgs e) => await MoveOrCopyAsync(false);

    private async void Copy_Click(object sender, RoutedEventArgs e) => await MoveOrCopyAsync(true);

    /// <summary>缩略图加载失败时收起图片容器，露出底层的类型图标（否则是一个空灰框）。</summary>
    private void Thumb_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image img && img.Parent is Border border) border.Visibility = Visibility.Collapsed;
    }

    /// <summary>右键先选中该行，否则 Vm.SelectedFile 为空会导致所有菜单项“没反应”。</summary>
    private void Item_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CloudFile file) return;
        Vm.SelectedFile = file;
        FileList.SelectedItem = file;
    }

    /// <summary>选择目标目录后移动/复制。</summary>
    private async Task MoveOrCopyAsync(bool copy)
    {
        if (Vm.SelectedFile is not { } file)
        {
            Vm.StatusText = "请先选中一个文件或文件夹";
            return;
        }

        var dirs = await Vm.GetDirectoryChoicesAsync();
        var choices = dirs.Where(d => d.Id != file.Id).ToList();

        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 300 };
        combo.Items.Add("根目录");
        foreach (var d in choices) combo.Items.Add(d.DirectoryName);
        combo.SelectedIndex = 0;

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = $"共 {choices.Count + 1} 个可选位置",
            FontSize = 12,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
        });
        panel.Children.Add(combo);

        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = copy ? $"复制「{file.Name}」到" : $"移动「{file.Name}」到",
            Content = panel,
            PrimaryButtonText = copy ? "复制" : "移动",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        var targetId = combo.SelectedIndex <= 0 ? "0" : choices[combo.SelectedIndex - 1].Id;
        await Vm.MoveOrCopyAsync(file, targetId, copy);
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        SetPickerWindow(picker);
        picker.FileTypeFilter.Add("*");
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0) return;
        await Vm.UploadAsync(files.Select(f => f.Path));
    }

    /// <summary>上传整个文件夹（递归建目录 + 上传内部文件）。</summary>
    private async void UploadFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is null) return;
        Vm.StatusText = $"开始上传文件夹：{folder.Name}";
        await Vm.UploadFolderAsync(folder.Path);
    }

    private async Task<Windows.Storage.StorageFolder?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        SetPickerWindow(picker);
        picker.FileTypeFilter.Add("*");
        return await picker.PickSingleFolderAsync();
    }

    // 未打包（unpackaged）应用需要手动关联 HWND，否则 Picker 会抛异常
    private void SetPickerWindow(object picker)
    {
        if (App.MainWin is null) return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWin);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }
}



