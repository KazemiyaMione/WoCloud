using Microsoft.Extensions.DependencyInjection;
using WoCloud.Client.Auth;
using WoCloud.Client.Core;
using WoCloud.Client.Files;
using WoCloud.Client.Member;
using WoCloud.Client.Transfer;
using WoCloud.ConsoleTest;

// ============================================================================
// 联通云盘协议冒烟测试
// 用法:
//   dotnet run --project src/WoCloud.ConsoleTest -- <accessToken>          # 已有 token 冒烟
//   dotnet run --project src/WoCloud.ConsoleTest -- sms <手机号>            # 短信登录全流程
//   dotnet run --project src/WoCloud.ConsoleTest -- qr                     # 扫码登录
// ============================================================================

var session = new SessionState();

if (args.Length >= 1 && args[0] == "sms")
{
    // 短信登录全流程
    var phone = args.Length > 1 ? args[1] : throw new ArgumentException("缺少手机号");
    await using var sp = CreateServices(session);
    var auth = sp.GetRequiredService<AuthApi>();
    var captcha = auth.NewCaptcha();
    Console.WriteLine($"向 {phone} 发送验证码...");
    var needCaptcha = await auth.IsCaptchaRequiredAsync(phone);
    if (needCaptcha)
    {
        Console.WriteLine("需要图形验证码，请打开图片输入文本:");
        Console.WriteLine(captcha.ImageUrl);
        var text = Console.ReadLine()?.Trim() ?? "";
        await auth.SendSmsCodeAsync(phone, (captcha.Uuid, text));
    }
    else
    {
        await auth.SendSmsCodeAsync(phone);
    }
    Console.Write("输入收到的 6 位短信验证码: ");
    var sms = Console.ReadLine()?.Trim() ?? "";
    var token = await auth.LoginBySmsAsync(phone, sms);
    Console.WriteLine($"登录成功! token = {token}");
    return;
}

if (args.Length >= 1 && args[0] == "qr")
{
    await using var sp = CreateServices(session);
    var auth = sp.GetRequiredService<AuthApi>();
    var ticket = await auth.GenerateQrCodeAsync();
    Console.WriteLine("请用联通云盘 APP 扫码（二维码 data uri 已存 qr.png.html）:");
    File.WriteAllText("qr.html",
        $"<img src=\"{ticket.ImageDataUri}\" width=280><p>{ticket.Uuid}</p>");
    Console.WriteLine($"票据: {ticket.Uuid}（2 秒轮询中...）");
    while (true)
    {
        var st = await auth.PollQrCodeAsync(ticket.Uuid);
        if (st == QrPollState.Confirmed) { Console.WriteLine("扫码登录成功!"); break; }
        if (st == QrPollState.Scanned) Console.WriteLine("已扫描，请在手机上确认...");
        await Task.Delay(2000);
    }
    // fallthrough 到冒烟
}
else if (args.Length >= 1 && args[0] != "sms" && args[0] != "qr")
{
    session.SetToken(args[0]);
    Console.WriteLine("使用粘贴 token");
}
else if (!session.TryRestore())
{
    Console.WriteLine("用法: dotnet run -- <accessToken> | sms <手机号> | qr");
    return;
}
else
{
    Console.WriteLine("已从本地会话恢复登录态");
}

await using var services = CreateServices(session);
var files = services.GetRequiredService<FileApi>();
var member = services.GetRequiredService<MemberApi>();
var auth2 = services.GetRequiredService<AuthApi>();

Console.WriteLine("\n=== QueryDirectorys（目录列表）===");
var dirs = await files.ListDirectoriesAsync();
foreach (var d in dirs?.Directorys ?? [])
    Console.WriteLine($"  {d.Id}  {d.DirectoryName}");

Console.WriteLine("\n=== AppQueryUser（用户信息）===");
var user = await auth2.GetUserInfoAsync();
Console.WriteLine($"  {user?.UserId} / {user?.UserName}  注册于 {user?.RegisterTime}");

Console.WriteLine("\n=== QueryAllFiles（根目录文件）===");
var list = await files.ListAsync("0", 0, 20);
foreach (var f in list?.Files ?? [])
    Console.WriteLine($"  [{(f.IsDirectory ? "目录" : f.SizeText)}] {f.Name}");

Console.WriteLine("\n=== BFF 会员卡 ===");
var card = await member.GetMemberCardAsync();
Console.WriteLine($"  {card?.VipName} 有效期 {card?.ValidDate} 空间 {card?.Space}");

Console.WriteLine("\n全部冒烟通过 ✔");
return;

static ServiceProvider CreateServices(SessionState session)
    => new ServiceCollection().AddWoCloud(session).BuildServiceProvider();
