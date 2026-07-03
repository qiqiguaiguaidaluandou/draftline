using Draftline.Core.Enums;

namespace Draftline.Core.Models;

/// <summary>
/// 异常待跟进池中的一条（跨批次、不跨流程、不跨机器）。整批提交时挂起异常的行不回传，转入此池，
/// 仍记来源批次以便追溯，后续解决后可补处理、补回传。
/// </summary>
public sealed class ExceptionItem
{
    public required FlowType Flow { get; init; }

    /// <summary>行标识键（核价=物料编码、挑图=EBS-ID）。</summary>
    public required string RowKey { get; init; }

    public required string MaterialCode { get; init; }

    public string? DisplayName { get; init; }

    /// <summary>来源批次目录名。</summary>
    public required string SourceBatch { get; init; }

    /// <summary>所属产品线组。</summary>
    public string GroupName { get; init; } = "Default";

    public required string Reason { get; init; }

    public DateTime SuspendedAt { get; init; }
}
