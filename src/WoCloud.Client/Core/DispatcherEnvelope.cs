using System.Text.Json;
using System.Text.Json.Serialization;

namespace WoCloud.Client.Core;

// ---------- 请求信封 ----------

public sealed class DispatcherHeader
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("resTime")] public long ResTime { get; set; }
    [JsonPropertyName("reqSeq")] public int ReqSeq { get; set; }
    [JsonPropertyName("channel")] public string Channel { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("sign")] public string Sign { get; set; } = "";
}

public sealed class DispatcherBody
{
    [JsonPropertyName("param")] public string? Param { get; set; }

    /// <summary>前端两种标志位等价：secret 或 key = true。</summary>
    [JsonPropertyName("secret")] public bool? Secret { get; set; }

    [JsonPropertyName("key")] public bool? Key { get; set; }

    [JsonPropertyName("clientId")] public string? ClientId { get; set; }

    [JsonPropertyName("secretType")] public string? SecretType { get; set; }
}

public sealed class DispatcherRequest
{
    [JsonPropertyName("header")] public DispatcherHeader Header { get; set; } = new();
    [JsonPropertyName("body")] public DispatcherBody Body { get; set; } = new();
}

// ---------- 响应信封 ----------

public sealed class DispatcherRsp
{
    [JsonPropertyName("RSP_CODE")] public string Code { get; set; } = "";
    [JsonPropertyName("RSP_DESC")] public string? Desc { get; set; }
    [JsonPropertyName("DATA")] public JsonElement? Data { get; set; }
}

public sealed class DispatcherResponse
{
    [JsonPropertyName("STATUS")] public string? Status { get; set; }
    [JsonPropertyName("MSG")] public string? Msg { get; set; }
    [JsonPropertyName("LOGID")] public string? LogId { get; set; }
    [JsonPropertyName("RSP")] public DispatcherRsp? Rsp { get; set; }
}
