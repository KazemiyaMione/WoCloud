using WoCloud.Client.Core;
using Xunit;

namespace WoCloud.Client.Tests;

public class CryptoHelperTests
{
    /// <summary>已知向量：GetZoneInfo 请求体加密结果。</summary>
    [Fact]
    public void Md5Sign_KnownVector_MatchesProduction()
    {
        var sign = CryptoHelper.Md5Hex("GetZoneInfo" + "1793756013121" + "122410" + "wohome" + "");
        Assert.Equal("2800febd015bf8cea654a59929d98b0f", sign);
    }

    /// <summary>QueryAllFiles 密文解密向量（token 前 16 位作密钥）。</summary>
    [Fact]
    public void AesDecrypt_QueryAllFilesVector_MatchesProduction()
    {
        const string token = "127fd37e-0247-40da-a12f-4a9eade5fe39";
        var plain = CryptoHelper.Decrypt(
            "PXzReF9upMjJh25FPlUnDJ4rgm31qyQE+vXkNXmsZMlH7JICfiMRdCHExNHNetrI/WLfTt8Oo2aP222mxiLvGWK5ENZLxVzb4UjrRaTYr2oPw5D/xhhcjxhdtgipBjnE6qd5rYL4t+B1d1rNg9sAdqKC2c52kFnbtGudvWu3oGSUbjBFU0mzC/wRFsANCiSW",
            token);
        Assert.Contains("\"spaceType\":\"0\"", plain);
        Assert.Contains("parentDirectoryId", plain);
    }

    /// <summary>GetZoneInfo 密文解密向量。</summary>
    [Fact]
    public void AesDecrypt_GetZoneInfoVector_MatchesProduction()
    {
        const string token = "127fd37e-0247-40da-a12f-4a9eade5fe39";
        var plain = CryptoHelper.Decrypt(
            "DSfMs7Qg7CVjljLGoTSKbu9FoFCMq7G+q90t9YYw+Vv2hIfJ+pZV432jcLwlst2T", token);
        Assert.Equal("{\"appId\":\"10000001\",\"clientId\":\"1001000021\"}", plain);
    }

    /// <summary>api-user 通道密文解密向量。</summary>
    [Fact]
    public void AesDecrypt_ApiUserSecret_MatchesProduction()
    {
        var plain = CryptoHelper.Decrypt(
            "/OP/co9mYqLkkDks24f5w2UqjAtM0VVpdVnlIDaAZVPu5gn9NOVLXU/P1XVTQz1pU4Cq5iy/VFMcmhl+nRZUAg==",
            "XFmi9GS2hzk98jGX");
        Assert.Contains("accessToken", plain);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip()
    {
        const string secret = "127fd37e-0247-40da-a12f-4a9eade5fe39";
        const string plain = "{\"spaceType\":\"0\",\"parentDirectoryId\":\"0\",\"pageNum\":0,\"pageSize\":50,\"中文\":\"测试✓\"}";
        var cipher = CryptoHelper.Encrypt(plain, secret);
        Assert.Equal(plain, CryptoHelper.Decrypt(cipher, secret));
    }

    [Theory]
    [InlineData("1234567890123456", "1234567890123456")]   // 恰好 16
    [InlineData("127fd37e-0247-40da-a12f-4a9eade5fe39", "127fd37e-0247-40")] // 截断 16
    [InlineData("short", "short")]                          // 不足 16
    public void SuitableKey_TruncatesTo16(string input, string expected)
    {
        var key = System.Text.Encoding.UTF8.GetString(CryptoHelper.SuitableKey(input));
        Assert.Equal(expected, key);
    }
}
