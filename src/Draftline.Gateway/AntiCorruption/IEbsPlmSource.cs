using Draftline.Core.Enums;

namespace Draftline.Gateway.AntiCorruption;

/// <summary>
/// 取数防腐层：带工号向 EBS 取需求行、向 PLM 取图纸/"是否变更"。
/// 由 EbsPlmSource + PlmClient 调真实 EBS/PLM 接口实现（路线图 B1）。
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
