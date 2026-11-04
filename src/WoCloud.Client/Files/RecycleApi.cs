using WoCloud.Client.Core;
using WoCloud.Client.Models;

namespace WoCloud.Client.Files;

/// <summary>
/// 回收站：
/// QueryRecycleData / ReductionRecycleData / DeleteRecycleData / EmptyRecycleData。
/// 注意：分页字段是 <c>pageNo</c>（从 1 开始），返回值是数组而非 {list}。
/// </summary>
public sealed class RecycleApi
{
    private readonly DispatcherClient _dispatcher;
    private readonly SessionState _session;

    public RecycleApi(DispatcherClient dispatcher, SessionState session)
    {
        _dispatcher = dispatcher;
        _session = session;
    }

    private string ClientId => _session.ClientId;

    /// <summary>回收站列表（DATA 直接是数组）。</summary>
    public Task<List<RecycleItem>?> ListAsync(int pageNo = 1, int pageSize = 50, int sortRule = 0,
        CancellationToken ct = default) =>
        _dispatcher.CallAsync<List<RecycleItem>>("QueryRecycleData", new
        {
            clientId = ClientId,
            pageNo,
            pageSize,
            sortRule,
        }, ct: ct);

    /// <summary>还原到原位置。</summary>
    public Task ReduceAsync(IEnumerable<string> deleteNos, CancellationToken ct = default) =>
        _dispatcher.CallAsync<object>("ReductionRecycleData", new
        {
            clientId = ClientId,
            deleteNos = deleteNos.ToList(),
        }, ct: ct);

    /// <summary>永久删除。</summary>
    public Task DeleteAsync(IEnumerable<string> deleteNos, CancellationToken ct = default) =>
        _dispatcher.CallAsync<object>("DeleteRecycleData", new
        {
            clientId = ClientId,
            deleteNos = deleteNos.ToList(),
        }, ct: ct);

    /// <summary>清空回收站。</summary>
    public Task ClearAsync(CancellationToken ct = default) =>
        _dispatcher.CallAsync<object>("EmptyRecycleData", new
        {
            clientId = ClientId,
        }, ct: ct);
}
