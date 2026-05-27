using TZHJ.Core.Enums;

namespace TZHJ.Gateway.Stores;

/// <summary>
/// 回传审计存储。本地即状态模式下，这是唯一跨机/长期的可追溯抓手；
/// 同时供登录补拉判"该窗口是否已成功回传过"（避免删了已处理批次被重拉重传）。
/// 骨架用内存实现；上线落 PostgreSQL audit_log 表（结构见 AuditRecord）。
/// </summary>
public interface IAuditStore
{
    /// <summary>记一条成功回传，返回审计号。</summary>
    string Record(FlowType flow, string employeeId, string batchKey, DateTime windowStart, DateTime windowEnd, string target, int rowCount);

    /// <summary>查某窗口是否已成功回传过（补拉判据）。命中返回审计号。</summary>
    (bool Exists, string? AuditId) Find(FlowType flow, string employeeId, DateTime windowStart, DateTime windowEnd);
}
