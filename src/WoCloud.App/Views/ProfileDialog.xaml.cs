using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WoCloud.Client.Auth;
using WoCloud.Client.Models;

namespace WoCloud.App.Views;

/// <summary>个人资料：昵称 / 性别 / 生日 / 头像。</summary>
public sealed partial class ProfileDialog : ContentDialog
{
    private readonly AccountApi _account = App.Current.Services.GetRequiredService<AccountApi>();
    private readonly AuthApi _auth = App.Current.Services.GetRequiredService<AuthApi>();

    private CloudUserInfo? _user;
    private string _headUrl = "";
    private bool _saved;

    public ProfileDialog()
    {
        InitializeComponent();
        PrimaryButtonClick += OnSaveClick;
        Loaded += (_, _) => _ = LoadAsync();
    }

    /// <summary>保存成功后的最新头像地址（供调用方刷新右上角）。</summary>
    public string HeadUrl => _headUrl;

    public bool Saved => _saved;

    private async Task LoadAsync()
    {
        if (_user is not null) return;
        try
        {
            _user = await _auth.GetUserInfoAsync();
        }
        catch (Exception e)
        {
            ShowError("读取资料失败：" + e.Message);
            return;
        }

        if (_user is null) return;

        NameBox.Text = _user.UserName ?? "";
        _headUrl = _user.HeadUrl ?? "";
        await ShowAvatarAsync(_headUrl);

        SexBox.SelectedIndex = _user.Sex switch { "1" => 0, "2" => 1, _ => -1 };
        BirthdayPicker.Date = long.TryParse(_user.Birthday, out var ms) && ms > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private async Task ShowAvatarAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            using var http = new HttpClient();
            var bytes = await http.GetByteArrayAsync(url);
            await SetAvatarAsync(bytes);
        }
        catch
        {
            // 头像加载失败不影响编辑
        }
    }

    private async Task SetAvatarAsync(byte[] bytes)
    {
        var bmp = new BitmapImage();
        using var ms = new MemoryStream(bytes);
        await bmp.SetSourceAsync(ms.AsRandomAccessStream());
        AvatarImage.Source = bmp;
    }

    private async void PickAvatar_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".gif");
        picker.FileTypeFilter.Add(".bmp");

        if (App.MainWin is not null)
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWin));

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        try
        {
            ClearError();
            AvatarHint.Text = "头像上传中…";

            using var stream = await file.OpenStreamForReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();

            if (bytes.Length > 10 * 1024 * 1024)
            {
                ShowError("图片超过 10 MB，请压缩后再试");
                return;
            }

            var url = await _account.UploadAvatarAsync(bytes, file.Name, ContentTypeOf(file.FileType));
            await SetAvatarAsync(bytes);
            if (!string.IsNullOrEmpty(url)) _headUrl = url;
            AvatarHint.Text = "头像已更新，保存后同步到账号";
        }
        catch (Exception ex)
        {
            AvatarHint.Text = "jpg / png / gif，单张不超过 10 MB";
            ShowError("头像上传失败：" + ex.Message);
        }
    }

    private static string ContentTypeOf(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        _ => "image/jpeg",
    };

    private async void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ClearError();

        var name = NameBox.Text.Trim();
        if (name.Length is < 1 or > 20)
        {
            args.Cancel = true;
            ShowError("昵称长度需在 1-20 个字符之间");
            return;
        }

        if (SexBox.SelectedItem is not ComboBoxItem sexItem || sexItem.Tag is not string sex)
        {
            args.Cancel = true;
            ShowError("请选择性别");
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            var birthdayMs = BirthdayPicker.Date.ToUnixTimeMilliseconds();
            await _account.UpdateProfileAsync(name, sex, birthdayMs, _headUrl);
            _saved = true;
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            ShowError("保存失败：" + ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ShowError(string message)
    {
        ErrBar.Message = message;
        ErrBar.IsOpen = true;
    }

    private void ClearError() => ErrBar.IsOpen = false;
}
