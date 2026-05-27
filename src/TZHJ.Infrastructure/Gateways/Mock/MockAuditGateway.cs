using TZHJ.Core.Contracts;
using TZHJ.Core.Contracts.Http;
using TZHJ.Core.Enums;

namespace TZHJ.Infrastructure.Gateways.Mock;

/// <summary>
/// 占位审计查询：恒返回未命中。离线模式下补拉判据退化为"本地无该窗即补"，行为合理。
/// 真接入后替换为 HttpAuditGateway（查后端审计日志）。
/// </summary>
public sealed class MockAuditGateway : IAuditGateway
{
    public Task<AuditExistsResponse> ExistsAsync(
        FlowType flow, DateTime windowStart, DateTime windowEnd, CancellationToken ct = default) =>
        Task.FromResult(new AuditExistsResponse { Exists = false });
}
