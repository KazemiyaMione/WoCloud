using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WoCloud.Client.Auth;
using WoCloud.Client.Core;
using WoCloud.App.Services;

namespace WoCloud.App.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly AuthApi _auth;
    private readonly SessionState _session;
    private readonly ShellNav _nav;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dq =
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _smsCode = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _captchaNeeded;
    [ObservableProperty] private string _captchaCode = "";
    [ObservableProperty] private byte[]? _captchaImage;
    [ObservableProperty] private byte[]? _qrImage;
    [ObservableProperty] private string _qrHint = "点击「刷新二维码」开始扫码登录";
    [ObservableProperty] private string _tokenInput = "";
    [ObservableProperty] private int _smsCountdown;
    [ObservableProperty] private bool _qrBusy;

    private (string Uuid, string ImageUrl)? _captcha;
    private QrCodeTicket? _qrTicket;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _pollTimer;

    /// <summary>IsBusy 变化时同步通知派生属性，供 x:Bind OneWay 使用。</summary>
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    public bool IsNotBusy => !IsBusy;

    public event Action? LoginSucceeded;

    public LoginViewModel(AuthApi auth, SessionState session, ShellNav nav)
    {
        _auth = auth;
        _session = session;
        _nav = nav;
    }

    public bool CanSendSms => !IsBusy && SmsCountdown == 0 && Phone.Length == 11;

    // ---------- 扫码登录 ----------

    [RelayCommand]
    private async Task RefreshQrAsync()
    {
        QrBusy = true;
        try
        {
            _qrTicket = await _auth.GenerateQrCodeAsync();
            QrImage = Convert.FromBase64String(_qrTicket.ImageDataUri["data:image/png;base64,".Length..]);
            QrHint = "请用联通云盘 APP 扫描二维码";
            StartQrPolling();
        }
        catch (Exception e)
        {
            QrHint = "二维码生成失败：" + e.Message;
        }
        finally { QrBusy = false; }
    }

    private void StartQrPolling()
    {
        _pollTimer?.Stop();
        _pollTimer = _dq.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(2);
        _pollTimer.Tick += async (_, _) =>
        {
            if (_qrTicket is null) { _pollTimer?.Stop(); return; }
            try
            {
                var st = await _auth.PollQrCodeAsync(_qrTicket.Uuid);
                switch (st)
                {
                    case QrPollState.Scanned:
                        QrHint = "扫描成功，请在手机上确认登录…";
                        break;
                    case QrPollState.Confirmed:
                        _pollTimer.Stop();
                        QrHint = "登录成功！";
                        OnLoginSucceeded();
                        break;
                }
            }
            catch (Exception)
            {
                // 二维码过期或网络抖动：3 次连续失败再刷新
                QrHint = "二维码可能已过期，请刷新";
            }
        };
        _pollTimer.Start();
    }

    // ---------- 短信登录 ----------

    [RelayCommand]
    private async Task SendSmsAsync()
    {
        if (Phone.Length != 11) { StatusText = "请输入 11 位手机号"; return; }
        IsBusy = true; StatusText = "";
        try
        {
            (string, string)? captcha = CaptchaNeeded && _captcha is { } c && CaptchaCode.Length > 0
                ? (c.Uuid, CaptchaCode) : null;
            await _auth.SendSmsCodeAsync(Phone, captcha);
            StatusText = "验证码已发送";
            CaptchaNeeded = false;
            StartCountdown();
        }
        catch (Exception e)
        {
            StatusText = e.Message;
            if (e.Message.Contains("验证码") || e.Message.Contains("6006") || e.Message.Contains("6007"))
                await LoadCaptchaAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task LoadCaptchaAsync()
    {
        _captcha = _auth.NewCaptcha();
        CaptchaNeeded = true;
        try
        {
            using var hc = new System.Net.Http.HttpClient();
            CaptchaImage = await hc.GetByteArrayAsync(_captcha.Value.ImageUrl);
        }
        catch { CaptchaImage = null; }
    }

    [RelayCommand]
    private async Task LoginBySmsAsync()
    {
        if (SmsCode.Length != 6) { StatusText = "请输入 6 位短信验证码"; return; }
        IsBusy = true; StatusText = "";
        try
        {
            await _auth.LoginBySmsAsync(Phone, SmsCode);
            OnLoginSucceeded();
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    // ---------- Token 直通 ----------

    [RelayCommand]
    private void LoginByToken()
    {
        var token = TokenInput.Trim();
        if (token.Length < 32) { StatusText = "请粘贴完整的 WoCloud-Web-Token（UUID）"; return; }
        _auth.SetTokenDirect(token);
        OnLoginSucceeded();
    }

    // ---------- 私有 ----------

    private void StartCountdown()
    {
        SmsCountdown = 60;
        _dq.CreateTimer().Interval = TimeSpan.FromSeconds(1);
        var t = _dq.CreateTimer();
        t.Interval = TimeSpan.FromSeconds(1);
        t.Tick += (_, _) =>
        {
            SmsCountdown--;
            OnPropertyChanged(nameof(CanSendSms));
            if (SmsCountdown <= 0) t.Stop();
        };
        t.Start();
        OnPropertyChanged(nameof(CanSendSms));
    }

    private void OnLoginSucceeded()
    {
        StatusText = "登录成功";
        LoginSucceeded?.Invoke();
        _nav.NavigateToFiles();
    }
}
