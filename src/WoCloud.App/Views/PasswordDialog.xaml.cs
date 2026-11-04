using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WoCloud.Client.Auth;

namespace WoCloud.App.Views;

/// <summary>修改登录密码：短信验证码 + 新密码（对齐网页端 type=5 分支）。</summary>
public sealed partial class PasswordDialog : ContentDialog
{
    private readonly AccountApi _account = App.Current.Services.GetRequiredService<AccountApi>();
    private readonly AuthApi _auth = App.Current.Services.GetRequiredService<AuthApi>();

    private string _phone = "";
    private DispatcherQueueTimer? _timer;
    private int _left;

    public PasswordDialog()
    {
        InitializeComponent();
        PrimaryButtonClick += OnSaveClick;
        Loaded += (_, _) => _ = LoadPhoneAsync();
    }

    public bool Changed { get; private set; }

    private async Task LoadPhoneAsync()
    {
        if (_phone.Length > 0) return;
        try
        {
            var user = await _auth.GetUserInfoAsync();
            _phone = user?.UserId ?? "";
        }
        catch
        {
            // token 失效时下面按未读取处理
        }

        if (_phone.Length == 11)
            PhoneText.Text = $"账号：{_phone[..3]}****{_phone[7..]}";
        else
            PhoneText.Text = _phone.Length > 0 ? $"账号：{_phone}" : "无法读取账号信息，请重新登录后再试";
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        ClearError();
        if (_phone.Length == 0)
        {
            ShowError("未读取到手机号，请重新登录");
            return;
        }

        SendBtn.IsEnabled = false;
        try
        {
            var ok = await _auth.SendSmsCodeAsync(_phone);
            if (!ok)
            {
                SendBtn.IsEnabled = true;
                ShowError("验证码发送失败，请稍后重试");
                return;
            }
            StartCountdown(60);
        }
        catch (Exception ex)
        {
            SendBtn.IsEnabled = true;
            ShowError("验证码发送失败：" + ex.Message);
        }
    }

    private void StartCountdown(int seconds)
    {
        _left = seconds;
        _timer?.Stop();

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) =>
        {
            _left--;
            if (_left <= 0)
            {
                _timer?.Stop();
                SendBtn.Content = "获取验证码";
                SendBtn.IsEnabled = true;
                return;
            }
            SendBtn.Content = $"重新发送（{_left}s）";
        };
        _timer.Start();
        SendBtn.Content = $"重新发送（{_left}s）";
    }

    private async void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ClearError();

        var code = CodeBox.Text.Trim();
        var pwd = PwdBox.Password;
        var pwd2 = Pwd2Box.Password;

        if (code.Length != 6)
        {
            args.Cancel = true;
            ShowError("请输入 6 位短信验证码");
            return;
        }
        if (pwd.Length is < 6 or > 20)
        {
            args.Cancel = true;
            ShowError("新密码长度需在 6-20 位之间");
            return;
        }
        if (pwd != pwd2)
        {
            args.Cancel = true;
            ShowError("两次输入的新密码不一致");
            return;
        }
        if (_phone.Length == 0)
        {
            args.Cancel = true;
            ShowError("未读取到手机号，请重新登录");
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            await _account.ChangePasswordAsync(_phone, code, pwd);
            Changed = true;
            _timer?.Stop();
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            ShowError("修改失败：" + ex.Message);
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
