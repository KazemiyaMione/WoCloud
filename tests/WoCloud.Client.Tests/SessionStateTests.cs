using WoCloud.Client.Core;
using Xunit;

namespace WoCloud.Client.Tests;

public class SessionStateTests
{
    [Fact]
    public void ClientSecret_DefaultClientId_KnownValue()
    {
        var s = new SessionState();
        Assert.Equal("1001000021", s.ClientId);
        Assert.Equal("XFmi9GS2hzk98jGX", s.ClientSecret);
    }

    [Fact]
    public void SessionExpired_FiredOnNotify()
    {
        var s = new SessionState();
        var fired = false;
        s.SessionExpired += () => fired = true;
        s.NotifySessionExpired();
        Assert.True(fired);
    }

    [Fact]
    public void SaveRestore_RoundTrip()
    {
        // 沙箱环境可能禁写用户目录 → 指定测试存储目录（跟随测试输出目录）
        var storeDir = Path.Combine(AppContext.BaseDirectory, "test-data");
        Directory.CreateDirectory(storeDir);
        SessionState.OverrideStoreDirectory = storeDir;
        try
        {
            var s = new SessionState();
            s.SetLogin("test-token-1234", phone: "13800000000");
            var s2 = new SessionState();
            var ok = s2.TryRestore();
            Assert.True(ok);
            Assert.Equal("test-token-1234", s2.AccessToken);
            Assert.Equal("13800000000", s2.Phone);
            s.DeleteStore();
            Assert.False(new SessionState().TryRestore());
        }
        finally
        {
            SessionState.OverrideStoreDirectory = null;
        }
    }
}
