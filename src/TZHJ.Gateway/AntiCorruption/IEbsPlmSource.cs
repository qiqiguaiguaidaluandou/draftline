using TZHJ.Core.Enums;

namespace TZHJ.Gateway.AntiCorruption;

/// <summary>
/// 取数防腐层：带工号向 EBS 取需求行、向 PLM 取图纸/"是否变更"。
/// 本期由 FakeDataSource 顶替；真接口到位后换成调 EBS/PLM 的实现（路线图 B1），端点不动。
/// </summary>
public interface IEbsPlmSource
{
    /// <summary>取一个批次的全部行（含图纸字节，供端点取元数据）。</summary>
    Task<IReadOnlyList<SourceRow>> FetchRowsAsync(
        FlowType flow, string employeeId, DateTime windowStart, DateTime windowEnd, CancellationToken ct = default);

    /// <summary>取单张图纸字节（流式下载用）。找不到返回 null（→404，客户端按"缺失"处理）。</summary>
    Task<byte[]?> OpenDrawingAsync(
        FlowType flow, string employeeId, DateTime windowStart, DateTime windowEnd, string drawingId, CancellationToken ct = default);
}
