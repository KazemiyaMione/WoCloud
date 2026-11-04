using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace WoCloud.App.Views;

/// <summary>
/// 字符串 URL → BitmapImage。
/// WinUI 3 的 x:Bind 不做 string→ImageSource 隐式转换（会抛 ArgumentException 崩溃），
/// 必须走转换器。
/// </summary>
public class UrlToBitmapConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapImage> Cache = [];

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url)) return null;
        if (Cache.TryGetValue(url, out var cached)) return cached;
        try
        {
            var bmp = new BitmapImage { UriSource = new Uri(url) };
            Cache[url] = bmp; // 同一缩略图可能被多处引用，缓存复用
            return bmp;
        }
        catch
        {
            return null; // 非法/失效 URL 不显示，不影响页面
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>bool → Visibility 反转（true=Collapsed）。</summary>
public class BoolToVisCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Collapsed;
}

/// <summary>#RRGGBB / #AARRGGBB → 画笔（结果缓存，列表里同一类型复用同一实例）。</summary>
public class HexToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = [];

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string hex || hex.Length < 7) return Fallback;

        lock (Cache)
        {
            if (Cache.TryGetValue(hex, out var cached)) return cached;
        }

        try
        {
            var s = hex.TrimStart('#');
            byte a = 255;
            if (s.Length == 8) { a = System.Convert.ToByte(s[..2], 16); s = s[2..]; }
            var brush = new SolidColorBrush(Windows.UI.Color.FromArgb(
                a,
                System.Convert.ToByte(s[..2], 16),
                System.Convert.ToByte(s.Substring(2, 2), 16),
                System.Convert.ToByte(s.Substring(4, 2), 16)));

            lock (Cache) { Cache[hex] = brush; }
            return brush;
        }
        catch
        {
            return Fallback;
        }
    }

    private static SolidColorBrush Fallback => new(Windows.UI.Color.FromArgb(255, 138, 148, 160));

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>非空字符串 → Visible。</summary>
public class StringToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
