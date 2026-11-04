using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WoCloud.App.Services;

/// <summary>
/// 法律免责声明弹窗。首次登录/首次进入主界面时弹出，强制阅读 <see cref="WaitSeconds"/> 秒后
/// 才能点「同意」；不同意则直接退出程序。
/// </summary>
public static class DisclaimerDialog
{
    /// <summary>同意按钮的强制等待秒数。</summary>
    public const int WaitSeconds = 10;

    public const string Text = """
        WoCloud 是第三方开源客户端，与「中国联通」「联通云盘」及其关联公司不存在任何隶属、合作或背书关系。

        一、账号与凭据
        1. 使用你自己的联通云盘账号登录，登录凭据仅保存在本机（data/session.json），不会上传到任何第三方服务器。
        2. 请勿在公共电脑上保留登录状态；由此产生的账号风险由你自行承担。

        二、服务与接口
        1. 本软件通过非公开接口与服务端通信，接口、参数与限速策略可能随时变更，作者不保证任何功能长期可用。
        2. 因接口变更、服务端限流、账号被限制等导致的上传或下载失败，作者不承担责任。

        三、数据安全
        1. 上传、重命名、删除、清空回收站等操作会直接作用于云端数据，请操作前自行确认。
        2. 请自行备份重要文件，作者不对任何数据丢失、损坏或泄露负责。

        四、使用限制
        1. 仅限个人学习与研究使用，不得用于商业用途、批量抓取、二次分发或任何违法用途。
        2. 请遵守联通云盘的用户协议以及你所在地区的法律法规。

        五、免责
        本软件按「现状」提供，不附带任何明示或暗示的担保。使用本软件即表示你已阅读、理解并接受上述全部条款；
        如不同意，请点击「不同意并退出」并停止使用。
        """;

    /// <summary>弹出免责声明。返回 true 表示用户在等待结束后点击了同意。</summary>
    public static async Task<bool> ShowAsync(XamlRoot? root, bool requireConsent = true)
    {
        if (root is null) return false;

        var body = new TextBlock
        {
            Text = Text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 21,
        };

        var scroll = new ScrollViewer
        {
            Content = body,
            Height = 340,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 12, 0),
        };

        var hint = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.6,
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };

        var panel = new StackPanel();
        panel.Children.Add(scroll);
        panel.Children.Add(hint);

        var dlg = new ContentDialog
        {
            XamlRoot = root,
            Title = "法律免责声明",
            Content = panel,
            DefaultButton = ContentDialogButton.None,
        };

        if (!requireConsent)
        {
            // 已同意过，从「设置 → 关于」再次查看：只读，不需要倒计时
            dlg.CloseButtonText = "关闭";
            hint.Text = $"条款版本 v{AppInfo.DisclaimerVersion} · 你已阅读并同意过本条款";
            _ = await dlg.ShowAsync();
            return false;
        }

        dlg.CloseButtonText = "不同意并退出";
        dlg.IsPrimaryButtonEnabled = false;

        var left = WaitSeconds;
        dlg.PrimaryButtonText = $"同意并继续（{left}s）";
        hint.Text = $"请完整阅读以上条款，{left} 秒后可以同意。";

        var timer = dlg.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.IsRepeating = true;
        timer.Tick += (_, _) =>
        {
            left--;
            if (left > 0)
            {
                dlg.PrimaryButtonText = $"同意并继续（{left}s）";
                hint.Text = $"请完整阅读以上条款，{left} 秒后可以同意。";
                return;
            }

            timer.Stop();
            dlg.PrimaryButtonText = "同意并继续";
            dlg.IsPrimaryButtonEnabled = true;
            hint.Text = "已阅读完毕，可以同意。";
        };
        timer.Start();

        var result = await dlg.ShowAsync();
        timer.Stop();
        return result == ContentDialogResult.Primary;
    }
}
