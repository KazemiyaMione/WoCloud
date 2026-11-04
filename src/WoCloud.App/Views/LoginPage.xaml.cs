using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using WoCloud.App.Services;
using WoCloud.App.ViewModels;

namespace WoCloud.App.Views;

public sealed partial class LoginPage : Page, INotifyPropertyChanged
{
    public LoginViewModel Vm { get; } = App.Current.Services.GetRequiredService<LoginViewModel>();

    public event PropertyChangedEventHandler? PropertyChanged;

    public LoginPage()
    {
        InitializeComponent();
        DataContext = Vm;
        // VM 任意状态变化 → 刷新页面级 x:Bind（QrEmpty/CaptchaBitmap 等）
        Vm.PropertyChanged += (_, _) => RaiseAll();
        _ = Vm.RefreshQrCommand.ExecuteAsync(null); // 自动出码
    }

    private void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    // ---------- X:Bind 辅助属性 ----------

    public Visibility QrEmpty => Vm.QrImage is null ? Visibility.Visible : Visibility.Collapsed;

    public BitmapImage? QrBitmap => Vm.QrImage is null ? null : ToBitmap(Vm.QrImage);

    public BitmapImage? CaptchaBitmap => Vm.CaptchaImage is null ? null : ToBitmap(Vm.CaptchaImage);

    public bool HasStatus => !string.IsNullOrEmpty(Vm.StatusText);

    private static BitmapImage ToBitmap(byte[] bytes)
    {
        var img = new BitmapImage();
        using var ms = new MemoryStream(bytes);
        img.SetSource(ms.AsRandomAccessStream());
        return img;
    }

    // ---------- 事件 ----------

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var idx = sender.Items.IndexOf(sender.SelectedItem);
        QrPanel.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        SmsPanel.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        TokenPanel.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Captcha_Tapped(object sender, TappedRoutedEventArgs e) =>
        _ = Vm.LoadCaptchaCommand.ExecuteAsync(null);
}

/// <summary>bool → Visibility 转换器（true=Visible）。</summary>
public class BoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Visible;
}

/// <summary>非空 → Visibility。</summary>
public class NullToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
