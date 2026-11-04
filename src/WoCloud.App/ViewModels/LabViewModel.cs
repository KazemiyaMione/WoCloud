using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WoCloud.Client.Core;
using WoCloud.Client.Misc;

namespace WoCloud.App.ViewModels;

/// <summary>
/// 调试面板：把底层请求入口集中暴露出来，直接填 JSON 参数调用，
/// 用于覆盖尚未封装成专用方法的接口（不改写服务端返回，原样展示）。
/// </summary>
public partial class LabViewModel : ObservableObject
{
    private readonly MiscApi _misc;

    /// <summary>前端源码中出现过的全部方法名（app.js 43 个 + 回收站 4 个 + 分享查看 3 个）。</summary>
    public ObservableCollection<string> Keys { get; } =
    [
        // 文件与目录
        "QueryAllFiles", "QueryTypeFiles", "QueryDirectorys", "GetSearchDirectory", "SearchFile",
        "CreateDirectory", "RenameFileOrDirectory", "DeleteFile", "MoveFile", "CopyFile",
        "GetDownloadUrl", "GetDownloadUrlV2", "GetDownloadUrlV3", "VideoDisplayFiles",
        "QueryUploadLog", "DeleteUploadLog", "GetZoneInfo", "ClassifyRule", "QuerySysConfig",
        "FamilyUserCurrentEncode", "QueryAlbumBackupDeviceList", "QueryAlbumBackupV2",
        "QrySysWcFileBackUp", "QueryWjsyBackup", "QueryMuid", "FeedBack",
        // 回收站
        "QueryRecycleData", "ReductionRecycleData", "DeleteRecycleData", "EmptyRecycleData",
        // 私密空间
        "PrivateSpaceLogin", "MoveInOrOutPrivateSpace", "ReSetPwd", "SetPwd", "VerifySetPwd",
        "GenerateReSetPwdSignCode",
        // 分享
        "AllShareFileList", "ShareFile", "CancelOrDelShareFile", "QueryShareInfo", "ShareFileDetail",
        "QueryShareFiles", "ReceiveShare", "ReportInfo",
        // 飞传
        "FlyPassFileList", "DelFlyPass", "ReceiveFlyPassBatch",
        // 用户
        "AppQueryUser", "PcLoginVerifyCode", "PcWebLoginVerifyCodeShow", "PcWebLogin",
        "AppLoginByMobile", "AppRegisterV2", "AppUpdateUserV2", "WebUpdateUserInfo",
        "AppRetrievePasswordV2", "AppLogout", "QueryFamilyGroups",
        // 会员
        "FCloudProductOrdListQry",
    ];

    public ObservableCollection<string> Channels { get; } = ["wohome", "api-user"];

    [ObservableProperty] private string _selectedKey = "QueryDirectorys";
    [ObservableProperty] private string _selectedChannel = "wohome";
    [ObservableProperty] private string _paramJson = "{\n  \"spaceType\": \"0\"\n}";
    [ObservableProperty] private bool _useEncrypt = true;
    [ObservableProperty] private bool _useClientSecret;
    [ObservableProperty] private string _resultText = "";
    [ObservableProperty] private bool _isBusy;

    public LabViewModel(MiscApi misc) => _misc = misc;

    /// <summary>按方法名给出常用参数模板，减少手填。</summary>
    partial void OnSelectedKeyChanged(string value)
    {
        ParamJson = value switch
        {
            "QueryAllFiles" or "QueryTypeFiles" =>
                "{\n  \"clientId\": \"1001000021\",\n  \"spaceType\": \"0\",\n  \"parentDirectoryId\": \"0\",\n  \"pageNum\": 0,\n  \"pageSize\": 20\n}",
            "QueryDirectorys" => "{\n  \"clientId\": \"1001000021\",\n  \"spaceType\": \"0\"\n}",
            "QueryRecycleData" => "{\n  \"clientId\": \"1001000021\",\n  \"pageNo\": 1,\n  \"pageSize\": 20,\n  \"sortRule\": 0\n}",
            "FlyPassFileList" => "{\n  \"clientId\": \"1001000021\",\n  \"pageNum\": 0,\n  \"pageSize\": 20\n}",
            "SearchFile" => "{\n  \"clientId\": \"1001000021\",\n  \"searchType\": \"2\",\n  \"keyWord\": \"png\",\n  \"pageNo\": 1,\n  \"pageSize\": 20\n}",
            "QueryShareInfo" => "{\n  \"shareId\": \"<shareId>\"\n}",
            "ShareFileDetail" => "{\n  \"shareId\": \"<shareId>\",\n  \"shareCode\": \"<提取码>\"\n}",
            "ClassifyRule" or "QueryMuid" or "QueryUploadLog" => "{\n  \"clientId\": \"1001000021\"\n}",
            "QueryFamilyGroups" or "AppQueryUser" or "AppLogout" => "{\n  \"accessToken\": \"<token>\"\n}",
            _ => "{\n  \"clientId\": \"1001000021\"\n}",
        };
    }

    public async Task InvokeAsync()
    {
        IsBusy = true;
        try
        {
            var r = await _misc.InvokeRawAsync(SelectedKey, ParamJson, UseEncrypt, SelectedChannel,
                UseClientSecret ? SecretType.ClientSecret : SecretType.Token);
            ResultText = $"CODE = {r.Code}\nDESC = {r.Desc}\nLOGID = {r.LogId}\n\n" +
                         (r.Data is null ? "(无 DATA)" : Pretty(r.Data));
        }
        catch (Exception e)
        {
            ResultText = "调用异常：" + e.GetType().Name + "\n" + e.Message;
        }
        finally { IsBusy = false; }
    }

    private static string Pretty(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return System.Text.Json.JsonSerializer.Serialize(doc.RootElement,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                });
        }
        catch { return json; }
    }
}
