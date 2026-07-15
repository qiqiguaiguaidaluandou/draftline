using Draftline.Core.Contracts.Http;

namespace Draftline.Core.Contracts;

/// <summary>
/// 用户操作日志网关：查询本人操作记录。操作员在 App 内只看自己（后端按令牌工号过滤），
/// 管理员在服务器侧查全部（后台 /api/admin/logs）。数据源是服务端各端点写的权威动作日志
/// （ActivityLogs，白名单见 <see cref="Draftline.Core.Logging.LogText.OperatorActions"/>）——
/// 客户端不再单独上报行为埋点（#030 阶段 2 移除，与端点动作日志高度冗余）。
/// 由 HttpOperationLogGateway 实现（GET /api/oplog/mine）。
/// </summary>
public interface IOperationLogGateway
{
    /// <summary>查当前操作员本人的操作记录（新到旧）。</summary>
    Task<IReadOnlyList<OperationLogEntry>> ListMineAsync(CancellationToken ct = default);
}
