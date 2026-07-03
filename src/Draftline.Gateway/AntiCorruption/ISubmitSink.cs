using Draftline.Core.Contracts;
using Draftline.Core.Enums;

namespace Draftline.Gateway.AntiCorruption;

/// <summary>
/// 回传防腐层：按流程把正常行回传到目标系统（核价→SRM / 挑图→EBS）。
/// 本期由 FakeDataSource 顶替；真接口到位后换成调 SRM/EBS 的实现（路线图 B2），端点不动。
/// </summary>
public interface ISubmitSink
{
    /// <summary>回传整批正常行，返回逐行结果。整批失败时抛异常或返回全失败由实现决定。</summary>
    Task<IReadOnlyList<SubmitRowResult>> SubmitAsync(
        FlowType flow, string employeeId, IReadOnlyList<SubmitRow> rows, CancellationToken ct = default);

    /// <summary>本批是否整体失败（占位演示用）。真实现按目标系统返回判定。</summary>
    bool ShouldFailBatch();
}
