using WoCloud.Client.Auth;
using Xunit;

namespace WoCloud.Client.Tests;

/// <summary>
/// 裸接口响应解析回归测试。
/// 背景：sendMessageCodeBase 返回的是 <c>{"STATUS":"200","RSP":{"RSP_CODE":"0000",…}}</c>，
/// 之前只认 BFF 的 <c>meta.code</c>，导致短信实际已下发、界面却提示「短信发送失败」。
/// </summary>
public class RawEnvelopeTests
{
    [Fact]
    public void ApiUserSuccess_IsParsedAsOk()
    {
        const string json = """
            {"STATUS":"200","MSG":"服务调用成功！","LOGID":"x",
             "RSP":{"RSP_CODE":"0000","RSP_DESC":"发送成功","DATA":null}}
            """;

        var (ok, code, message, data) = AuthApi.ParseRawEnvelope(json);

        Assert.True(ok);
        Assert.Equal("0000", code);
        Assert.Equal("发送成功", message);
        Assert.Null(data);
    }

    [Fact]
    public void ApiUserRiskControl_KeepsServerMessage()
    {
        const string json = """
            {"STATUS":"200","RSP":{"RSP_CODE":"6006","RSP_DESC":"请输入图形验证码","DATA":{"isNeedVerfyCode":1}}}
            """;

        var (ok, code, message, data) = AuthApi.ParseRawEnvelope(json);

        Assert.False(ok);
        Assert.Equal("6006", code);
        Assert.Equal("请输入图形验证码", message);
        Assert.NotNull(data);
        Assert.Equal(1, data!.Value.GetProperty("isNeedVerfyCode").GetInt32());
    }

    [Fact]
    public void BffShape_IsParsedAsOk()
    {
        const string json = """{"meta":{"code":"200"},"result":{"vipLevel":"3"}}""";

        var (ok, code, _, data) = AuthApi.ParseRawEnvelope(json);

        Assert.True(ok);
        Assert.Equal("200", code);
        Assert.Equal("3", data!.Value.GetProperty("vipLevel").GetString());
    }

    [Fact]
    public void BffFailure_IsParsedAsNotOk()
    {
        const string json = """{"meta":{"code":"500","message":"系统繁忙"}}""";

        var (ok, code, message, _) = AuthApi.ParseRawEnvelope(json);

        Assert.False(ok);
        Assert.Equal("500", code);
        Assert.Equal("系统繁忙", message);
    }

    [Fact]
    public void Garbage_IsReportedAsBadJson()
    {
        var (ok, code, _, _) = AuthApi.ParseRawEnvelope("<html>502</html>");

        Assert.False(ok);
        Assert.Equal("BAD_JSON", code);
    }
}
