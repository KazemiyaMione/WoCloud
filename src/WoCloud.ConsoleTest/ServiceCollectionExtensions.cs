using Microsoft.Extensions.DependencyInjection;
using WoCloud.Client.Auth;
using WoCloud.Client.Core;
using WoCloud.Client.Files;
using WoCloud.Client.Member;
using WoCloud.Client.Share;
using WoCloud.Client.Transfer;

namespace WoCloud.ConsoleTest;

/// <summary>ConsoleTest 服务注册（与 WoCloud.App 保持一致的组合方式）。</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWoCloud(this IServiceCollection services, SessionState? session = null)
    {
        var s = session ?? new SessionState();
        services.AddSingleton(s);
        services.AddSingleton<DispatcherClient>();
        services.AddSingleton<AuthApi>();
        services.AddSingleton<FileApi>();
        services.AddSingleton<DownloadClient>();
        services.AddSingleton<UploadClient>();
        services.AddSingleton<ShareApi>();
        services.AddSingleton<MemberApi>();
        return services;
    }
}
