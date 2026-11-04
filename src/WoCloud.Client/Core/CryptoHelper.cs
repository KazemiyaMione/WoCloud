using System.Security.Cryptography;
using System.Text;

namespace WoCloud.Client.Core;

/// <summary>
/// 协议层加解密工具：MD5 签名 + AES-128-CBC/PKCS7（对应前端 oe/90c5 模块）。
/// 密钥规则：长度 &gt; 16 时截取前 16 位（getSuitableKey）；IV 固定。
/// </summary>
public static class CryptoHelper
{
    /// <summary>前端 bridge.js 混淆变量 obfuscatorAESIv，全站固定。</summary>
    public const string FixedIv = "wNSOYIB1k1DjY5lA";

    /// <summary>sign = MD5(key + resTime + reqSeq + channel + version)，32 位小写。</summary>
    public static string Md5Hex(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 密钥规整（getSuitableKey）：超过 16 字符取前 16 位；不足 16 位用空格右补齐——
    /// 与手机端 <c>str(token).ljust(16)[:16]</c> 一致（HandheldHallAutoLoginV2 前的空 token 场景）。
    /// </summary>
    public static byte[] SuitableKey(string secret) =>
        Encoding.UTF8.GetBytes(secret.Length >= 16 ? secret[..16] : secret.PadRight(16));

    public static string Encrypt(string plainText, string secret, string iv = FixedIv)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = SuitableKey(secret);
        aes.IV = Encoding.UTF8.GetBytes(iv);
        using var enc = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var data = enc.TransformFinalBlock(plainBytes, 0, plainBytes.Length); // 注意：字节数而非字符数
        return Convert.ToBase64String(data);
    }

    public static string Decrypt(string base64CipherText, string secret, string iv = FixedIv)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = SuitableKey(secret);
        aes.IV = Encoding.UTF8.GetBytes(iv);
        using var dec = aes.CreateDecryptor();
        var cipher = Convert.FromBase64String(base64CipherText);
        var data = dec.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(data);
    }
}
