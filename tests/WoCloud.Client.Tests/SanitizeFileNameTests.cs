using WoCloud.Client.Transfer;

using Xunit;

namespace WoCloud.Client.Tests;

/// <summary>云端文件名落盘清洗（结尾点/空格 → FileStream 报「拒绝访问」的根因）。</summary>
public class SanitizeFileNameTests
{
    [Theory]
    [InlineData("捕获.jpg", "捕获.jpg")]                    // 正常名不动
    [InlineData("file.jpg.", "file.jpg")]                  // 结尾点 → 拒绝访问的元凶
    [InlineData("file.jpg ", "file.jpg")]                  // 结尾空格 → 同上
    [InlineData("a:b*c?d\"e|f", "a_b_c_d_e_f")]            // 非法符号统一替换
    [InlineData("bad\tname.jpg", "bad_name.jpg")]          // 控制符
    [InlineData("...", "未命名文件")]                       // 清洗后为空兜底
    public void SanitizeFileName_ReplacesIllegalChars(string input, string expected)
    {
        Assert.Equal(expected, DownloadClient.SanitizeFileName(input));
    }
}
