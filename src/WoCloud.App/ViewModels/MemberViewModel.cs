using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using WoCloud.App.Services;
using WoCloud.Client.Auth;
using WoCloud.Client.Member;
using WoCloud.Client.Misc;
using WoCloud.Client.Models;

namespace WoCloud.App.ViewModels;

/// <summary>会员空间页：账号信息（AppQueryUser）+ 会员摘要（BFF card/shouting）+ 家庭云群组。</summary>
public partial class MemberViewModel : ObservableObject
{
    private readonly MemberApi _member;
    private readonly AuthApi _auth;
    private readonly MiscApi _misc;
    private readonly AppSettings _settings;

    public ObservableCollection<FamilyGroup> Families { get; } = [];

    [ObservableProperty] private MemberSummary? _summary;
    [ObservableProperty] private MembershipBenefit? _benefit;
    [ObservableProperty] private CloudUserInfo? _user;
    [ObservableProperty] private BitmapImage? _avatar;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "";

    public MemberViewModel(MemberApi member, AuthApi auth, MiscApi misc, AppSettings settings)
    {
        _member = member;
        _auth = auth;
        _misc = misc;
        _settings = settings;
    }

    // ---------------- 账号 ----------------

    public string UserName => User?.UserName is { Length: > 0 } n ? n : "我的账号";
    public string UserIdText => string.IsNullOrEmpty(User?.UserId) ? "" : "ID " + User.UserId;
    public bool HasAvatar => Avatar is not null;
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);
    public bool IsNotLoading => !IsLoading;

    // ---------------- 会员 ----------------

    public bool HasSummary => Summary is not null;
    public bool IsVip => Summary is { VipLevel: > 0 };
    public string LevelText => Summary?.LevelText ?? "普通用户";
    public string VipBadge => LevelText;
    public string RenewText => Summary?.RenewText ?? "—";
    public string ValidText => string.IsNullOrWhiteSpace(Summary?.ValidDate) ? "—" : Summary!.ValidDate!;

    // ---------------- 权益 ----------------

    public bool HasBenefit => Benefit is not null;
    public string BenefitVipText => Benefit?.VipDesc ?? "—";
    public string BenefitUploadText => Benefit?.UploadLimitText ?? "—";
    public string BenefitRecycleText => Benefit?.RecycleText ?? "—";
    public string BenefitFamilyText => Benefit?.FamilyText ?? "—";
    public string BenefitTransientText => Benefit?.TransientText ?? "—";
    public string BenefitBatchText => Benefit?.BatchText ?? "—";
    public string BenefitExpireText => Benefit?.ExpireText ?? "—";

    // ---------------- 每日打卡（纯本地记录，不与云端权益关联） ----------------

    private static string TodayText => DateTime.Today.ToString("yyyy-MM-dd");

    public bool CheckedInToday => _settings.LastCheckInDate == TodayText;
    public bool CanCheckIn => !CheckedInToday;
    public string CheckInButtonText => CheckedInToday ? "今日已打卡" : "立即打卡";
    public int CheckInStreak => _settings.CheckInStreak;
    public int CheckInTotal => _settings.CheckInTotal;
    public string CheckInHint => CheckedInToday
        ? $"连续 {CheckInStreak} 天 · 累计 {CheckInTotal} 天，明天记得再来"
        : CheckInStreak > 0
            ? $"上次已连续 {CheckInStreak} 天，今天别断了"
            : "今天还没打卡";

    /// <summary>打卡：当天重复点击无效；断签（昨天没打）则从 1 重新计数。</summary>
    [RelayCommand]
    private void CheckIn()
    {
        var today = DateTime.Today;
        var todayStr = today.ToString("yyyy-MM-dd");
        if (_settings.LastCheckInDate == todayStr) return;

        var yesterdayStr = today.AddDays(-1).ToString("yyyy-MM-dd");
        _settings.CheckInStreak = _settings.LastCheckInDate == yesterdayStr ? _settings.CheckInStreak + 1 : 1;
        _settings.CheckInTotal += 1;
        _settings.LastCheckInDate = todayStr;   // 属性变更即落盘

        NotifyCheckIn();
    }

    private void NotifyCheckIn()
    {
        OnPropertyChanged(nameof(CheckedInToday));
        OnPropertyChanged(nameof(CanCheckIn));
        OnPropertyChanged(nameof(CheckInButtonText));
        OnPropertyChanged(nameof(CheckInStreak));
        OnPropertyChanged(nameof(CheckInTotal));
        OnPropertyChanged(nameof(CheckInHint));
    }

    // ---------------- 空间 ----------------

    public string UsedText => Summary?.UsedText ?? "—";
    public string AllText => Summary?.AllText ?? "—";
    public string RemainingText => Summary is null
        ? "—"
        : CloudFile.FormatSize(Math.Max(0, Summary.AllBytes - Summary.UsedBytes));
    public string PercentText => Summary is null ? "—" : $"{Summary.UsedRatio * 100:0.#}%";
    public double SpaceRatio => Math.Clamp(Summary?.UsedRatio ?? 0, 0, 1);
    public bool SpaceWarning => SpaceRatio >= 0.85;

    public bool HasFamilies => Families.Count > 0;
    public string FamilySummary => Families.Count == 0 ? "未加入家庭云" : $"{Families.Count} 个家庭";

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusText = "";
        try
        {
            User = await _auth.GetUserInfoAsync();
            Avatar = await LoadBitmapAsync(User?.HeadUrl);

            Summary = await _member.GetMemberSummaryAsync();
            if (Summary is null)
                StatusText = "会员信息暂不可用（服务端对当前登录方式的会话有限制），空间用量可能显示为 —";

            try { Benefit = await _member.GetBenefitAsync(); }
            catch { Benefit = null; }   // 权益拿不到不影响主信息

            if (Avatar is null) Avatar = await LoadBitmapAsync(Summary?.HeadImageUrl);

            Families.Clear();
            var groups = await _misc.GetFamilyGroupsAsync();
            foreach (var g in groups?.All ?? []) Families.Add(g);
        }
        catch (Exception e)
        {
            StatusText = "加载失败：" + e.Message;
        }
        finally
        {
            IsLoading = false;
            NotifyDerived();
        }
    }

    /// <summary>在浏览器中打开云盘会员中心（改套餐/续费只能在官网操作）。</summary>
    [RelayCommand]
    private async Task OpenPortalAsync()
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://pan.wo.cn/"));
        }
        catch (Exception e)
        {
            StatusText = "打开官网失败：" + e.Message;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    // ---------------- 通知 ----------------

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(UserName));
        OnPropertyChanged(nameof(UserIdText));
        OnPropertyChanged(nameof(HasAvatar));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(IsNotLoading));
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(IsVip));
        OnPropertyChanged(nameof(LevelText));
        OnPropertyChanged(nameof(VipBadge));
        OnPropertyChanged(nameof(RenewText));
        OnPropertyChanged(nameof(ValidText));
        OnPropertyChanged(nameof(UsedText));
        OnPropertyChanged(nameof(AllText));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(PercentText));
        OnPropertyChanged(nameof(SpaceRatio));
        OnPropertyChanged(nameof(SpaceWarning));
        OnPropertyChanged(nameof(HasFamilies));
        OnPropertyChanged(nameof(FamilySummary));
        NotifyBenefit();
    }

    private void NotifyBenefit()
    {
        OnPropertyChanged(nameof(HasBenefit));
        OnPropertyChanged(nameof(BenefitVipText));
        OnPropertyChanged(nameof(BenefitUploadText));
        OnPropertyChanged(nameof(BenefitRecycleText));
        OnPropertyChanged(nameof(BenefitFamilyText));
        OnPropertyChanged(nameof(BenefitTransientText));
        OnPropertyChanged(nameof(BenefitBatchText));
        OnPropertyChanged(nameof(BenefitExpireText));
    }

    partial void OnBenefitChanged(MembershipBenefit? value) => NotifyBenefit();

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsNotLoading));

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    partial void OnSummaryChanged(MemberSummary? value) => NotifyDerived();

    partial void OnUserChanged(CloudUserInfo? value) => NotifyDerived();

    partial void OnAvatarChanged(BitmapImage? value) => OnPropertyChanged(nameof(HasAvatar));

    /// <summary>头像先下载字节再解码（直链直接用 URI 绑定时常因鉴权/防盗链空白）。</summary>
    private static async Task<BitmapImage?> LoadBitmapAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            using var http = new HttpClient();
            var bytes = await http.GetByteArrayAsync(url);
            if (bytes.Length == 0) return null;
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(ms.AsRandomAccessStream());
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
