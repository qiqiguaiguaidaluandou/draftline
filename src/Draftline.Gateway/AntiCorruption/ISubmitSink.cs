using Draftline.Core.Contracts;
using Draftline.Core.Enums;

namespace Draftline.Gateway.AntiCorruption;

/// <summary>
/// 回传防腐层：按流程把正常行回传到目标系统（核价→SRM 价格；挑图→EBS 机加结果 CUX_AI_MACH_DRW_RST）。
/// </summary>
public interface ISubmitSink
{
    /// <summary>回传整批正常行，返回逐行结果。整批失败时抛异常（端点按可重试处理）。</summary>
    Task<IReadOnlyList<SubmitRowResult>> SubmitAsync(
        FlowType flow, string employeeId, IReadOnlyList<SubmitRow> rows, CancellationToken ct = default);
}
