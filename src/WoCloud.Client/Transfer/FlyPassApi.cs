using WoCloud.Client.Core;
using WoCloud.Client.Models;

namespace WoCloud.Client.Transfer;

/// <summary>
/// 飞传（列表、删除、接收）：
/// FlyPassFileList / DelFlyPass / ReceiveFlyPassBatch。
/// </summary>
public sealed class FlyPassApi
{
    private readonly DispatcherClient _dispatcher;
    private readonly SessionState _session;

    public FlyPassApi(DispatcherClient dispatcher, SessionState session)
    {
        _dispatcher = dispatcher;
        _session = session;
    }

    private string ClientId => _session.ClientId;

    /// <summary>飞传列表（DATA = {list}）。</summary>
    public Task<FlyPassListResult?> ListAsync(int pageNum = 0, int pageSize = 50,
        CancellationToken ct = default) =>
        _dispatcher.CallAsync<FlyPassListResult>("FlyPassFileList", new
        {
            clientId = ClientId,
            pageNum,
            pageSize,
        }, ct: ct);

    /// <summary>删除飞传记录（注意 key 是单数 flyPassId，值为逗号串）。</summary>
    public Task DeleteAsync(IEnumerable<string> flyPassIds, CancellationToken ct = default) =>
        _dispatcher.CallAsync<object>("DelFlyPass", new
        {
            clientId = ClientId,
            flyPassId = string.Join(",", flyPassIds),
        }, ct: ct);

    /// <summary>接收（转存）飞传到目标目录。</summary>
    public Task ReceiveAsync(IEnumerable<string> flyPassIds, string targetDirId,
        CancellationToken ct = default) =>
        _dispatcher.CallAsync<object>("ReceiveFlyPassBatch", new
        {
            clientId = ClientId,
            flyPassIdList = flyPassIds.ToList(),
            targetDirId,
        }, ct: ct);
}
