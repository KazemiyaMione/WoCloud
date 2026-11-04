namespace WoCloud.Client.Core;

/// <summary>协议常量（改动需与其余常量保持同步，否则请求会被拒绝）。</summary>
public static class WoCloudConstants
{
    public const string BaseUrl = "https://panservice.mail.wo.cn";

    public const string WohomeDispatcher = BaseUrl + "/wohome/dispatcher";
    public const string ApiUserDispatcher = BaseUrl + "/api-user/dispatcher";

    public const string ChannelWohome = "wohome";
    public const string ChannelApiUser = "api-user";

    public const string DefaultClientId = "1001000021";
    public const string AppId = "10000001";

    /// <summary>图形验证码图片地址（uuid 客户端随机生成 v4）。</summary>
    public static string GetVerifyCodeUrl(string uuid) =>
        $"{BaseUrl}/api-user/getverifycode?uuid={uuid}";

    /// <summary>发送短信验证码（裸 JSON POST，param 为 AES 密文）。</summary>
    public const string SendMessageCodeUrl = BaseUrl + "/api-user/sendMessageCodeBase";
public const string UpdateHeadUrl = BaseUrl + "/api-user/update/user/head";

/// <summary>联通手机营业厅 App 票据兑换（App ecs_token → 云盘 ticket）。</summary>
public const string GetTicketByNativeUrl = "https://m.client.10010.com/edop_ng/getTicketByNative";

    /// <summary>扫码登录二维码（裸 JSON POST）。</summary>
    public const string QrCodeGenerateUrl = BaseUrl + "/wohome/open/v1/QRCode/generate";
    public const string QrCodeQueryUrl = BaseUrl + "/wohome/open/v1/QRCode/query";

    /// <summary>api-user 通道 AES 密钥映射（前端 f3f6 模块还原；key=clientId）。</summary>
    public static readonly IReadOnlyDictionary<string, string> ClientSecrets = new Dictionary<string, string>
    {
        ["1001000001"] = "R68VBxUs7Cv87VsN",
        ["1001000003"] = "Py1J67PAQoCb8Iel",
        ["1001000015"] = "M6vM8mMd1agRmExp",
        ["1001000017"] = "ecx8uEYhOGbpvCVp",
        ["1001000021"] = "XFmi9GS2hzk98jGX",
        ["1001000024"] = "32qIQvM4AUYrAPD8",
        ["1001000035"] = "iELf0UL07o6I8eRK",
        ["1001000039"] = "QS5ByWs4GsTY18J1",
        ["1001000040"] = "186R8gEbSJolWRl9",
        ["1001000099"] = "Py1J67PAQoCb8Iel",
        ["1001000210"] = "32QXmaEWaI5H56l5",
    };
}

/// <summary>加密密钥选择策略（对应 body.secretType 语义）。</summary>
public enum SecretType
{
    /// <summary>wohome 常规：密钥 = accessToken 前 16 位。</summary>
    Token,

    /// <summary>api-user 通道 / 未登录分享页使用按 clientId 查表的独立密钥。</summary>
    ClientSecret,
}
