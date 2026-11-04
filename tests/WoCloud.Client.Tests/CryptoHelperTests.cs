using WoCloud.Client.Core;
using Xunit;

namespace WoCloud.Client.Tests;

/// <summary>
/// 加解密与签名测试。
/// 所有向量均为构造的合成数据（假 token、假目录 id），不含任何真实账号信息。
/// </summary>
public class CryptoHelperTests
{
    /// <summary>合成 token：仅用于固定密钥推导行为，不对应任何真实会话。</summary>
    private const string SampleToken = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";

    [Fact]
    public void Md5Sign_KnownVector_MatchesProduction()
    {
        var sign = CryptoHelper.Md5Hex("GetZoneInfo" + "1700000000000" + "100000" + "wohome" + "");
        Assert.Equal("85cdb7f4dc7e0632a389857698d7d77e", sign);
    }

    /// <summary>QueryAllFiles 密文解密向量（token 前 16 位作密钥）。</summary>
    [Fact]
    public void AesDecrypt_QueryAllFilesVector_MatchesProduction()
    {
        var plain = CryptoHelper.Decrypt(
            "cw7gBNqNL9G1hc0iUPtS5eEHyfzyniO76hdotl9sJ2TnKLKTGvqQbCZ1R0JzoU7PZnUQlvdXxpcWoNlUAMfzO8V+aROBfSpgavhixF+FevzuKI+ww3BbyBuakfBVa8nDyKgVp9Xy43KkoaEFQd8KyWERxBFoGEGXB5yFMwMWBnIZViR16yZuPk+0oLXg7Mhw",
            SampleToken);
        Assert.Contains("\"spaceType\":\"0\"", plain);
        Assert.Contains("parentDirectoryId", plain);
    }

    /// <summary>GetZoneInfo 密文解密向量。</summary>
    [Fact]
    public void AesDecrypt_GetZoneInfoVector_MatchesProduction()
    {
        var plain = CryptoHelper.Decrypt(
            "uSFUxcYB0rGsLLq22BhOB1dukx5KNlH1to4hqtlJg0VR30UcczxsOlio+w5A66TQ", SampleToken);
        Assert.Equal("{\"appId\":\"10000001\",\"clientId\":\"1001000021\"}", plain);
    }

    /// <summary>api-user 通道密文解密向量。</summary>
    [Fact]
    public void AesDecrypt_ApiUserSecret_MatchesProduction()
    {
        var plain = CryptoHelper.Decrypt(
            "/OP/co9mYqLkkDks24f5w5vL4XoXNBRVMTM06SAlnc53Mtrqbtc0kIu101hO7a5YOI1Ugn6i7mkeCdcLLFWUfg==",
            "XFmi9GS2hzk98jGX");
        Assert.Contains("accessToken", plain);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip()
    {
        const string plain = "{\"spaceType\":\"0\",\"parentDirectoryId\":\"0\",\"pageNum\":0,\"pageSize\":50,\"中文\":\"测试✓\"}";
        var cipher = CryptoHelper.Encrypt(plain, SampleToken);
        Assert.Equal(plain, CryptoHelper.Decrypt(cipher, SampleToken));
    }

    [Theory]
    [InlineData("1234567890123456", "1234567890123456")]   // 恰好 16
    [InlineData("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", "aaaaaaaa-bbbb-4c")] // 截断 16
    [InlineData("short", "short           ")]              // 不足 16：空格右补齐（对齐手机端 ljust(16)）
    public void SuitableKey_TruncatesTo16(string input, string expected)
    {
        var key = System.Text.Encoding.UTF8.GetString(CryptoHelper.SuitableKey(input));
        Assert.Equal(expected, key);
    }
}
