using Draftline.Core.Enums;

namespace Draftline.Core.Contracts.Http;

/// <summary>
/// 同步清单项，描述一个批次的元数据。
/// </summary>
public sealed class BatchCatalogItem
{
    public required string BatchId { get; init; }
    public required string GroupName { get; init; }
    public FlowType Flow { get; init; }
    public BatchLocation Status { get; init; }
    public int TotalRows { get; init; } // 新增：物料总行数
    public DateTime LastModified { get; init; }
    public List<SyncFileMeta> Files { get; init; } = new();
}

public sealed class SyncFileMeta
{
    public required string FileName { get; init; }
    public long Size { get; init; }
    public DateTime LastModified { get; init; }
}

/// <summary>
/// 行数据更新请求。
/// </summary>
public sealed class UpdateRowRequest
{
    public required FlowType Flow { get; init; }
    public required string BatchId { get; init; }
    public required string GroupName { get; init; }
    public required string RowKey { get; init; }
    public required Dictionary<string, string?> Values { get; init; }

    /// <summary>
    /// 是否来自异常处理页的"补回传"链路。为 true 时服务端**照常写数据、但不单独写 UpdateRow 日志**——
    /// 行改动摘要经响应回给客户端、并入随后的 Resolve 日志，避免一次异常回传刷出重复的改行日志（见 #030）。
    /// 普通批次作业页的改行编辑保持 false，仍写带完整 diff 的 UpdateRow 日志。
    /// </summary>
    public bool IsExceptionResolve { get; init; }
}

/// <summary>行数据更新结果。</summary>
public sealed class UpdateRowResult
{
    /// <summary>本次实际发生变化的字段摘要（中文列名 + 老值→新值，如「目标价(10→12)」）；无变化则为空串。</summary>
    public string ChangeSummary { get; init; } = "";
}

/// <summary>
/// 批次状态更新请求（如：手动标记完成）。
/// </summary>
public sealed class UpdateBatchStatusRequest
{
    public required FlowType Flow { get; init; }
    public required string BatchId { get; init; }
    public required string GroupName { get; init; }
    public BatchLocation Status { get; init; }
}

/// <summary>
/// 按行重新获取图纸请求：取数时 PLM 还没传图、挂了异常，之后图纸补上了，
/// 据此向 PLM 重新拉取该料号图纸并落到来源批次文件夹。
/// </summary>
public sealed class RefetchDrawingRequest
{
    public required FlowType Flow { get; init; }
    public required string BatchId { get; init; }
    public required string GroupName { get; init; }
    public required string RowKey { get; init; }
    public required string MaterialCode { get; init; }
}

/// <summary>
/// 重新获取图纸结果。Found=false 表示 PLM 中仍无该料号图纸（提示"重新获取失败"）；
/// Found=true 时 Files 为新落盘的图纸文件名，客户端据此同步到本地。
/// </summary>
public sealed class RefetchDrawingResult
{
    public bool Found { get; init; }
    public List<string> Files { get; init; } = new();
    public string? Message { get; init; }
}

/// <summary>
/// 异常挂起请求。
/// </summary>
public sealed class SuspendExceptionRequest
{
    public required FlowType Flow { get; init; }
    public required string BatchId { get; init; }
    public required string GroupName { get; init; }
    public required string RowKey { get; init; }
    public required string MaterialCode { get; init; }
    public string? DisplayName { get; init; }
    public required string Reason { get; init; }
}
