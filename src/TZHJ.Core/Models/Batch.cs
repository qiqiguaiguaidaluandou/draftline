using TZHJ.Core.Enums;

namespace TZHJ.Core.Models;

/// <summary>
/// 一个批次 = 一个采集时间窗。批次键 = (流程类型 + 窗口起止)，防重复。
/// 批次级状态由所在文件夹（<see cref="Location"/>）表示，文件夹即真相源。
/// </summary>
public sealed class Batch
{
    public required FlowType Flow { get; init; }

    public required string EmployeeId { get; init; }

    /// <summary>采集窗口起（含）。</summary>
    public required DateTime WindowStart { get; init; }

    /// <summary>采集窗口止（含）。</summary>
    public required DateTime WindowEnd { get; init; }

    /// <summary>批次目录名（如 20260519_1531-20260520_0930），见 <see cref="BatchKey"/>。</summary>
    public required string FolderName { get; init; }

    /// <summary>批次目录绝对路径。</summary>
    public required string FolderPath { get; init; }

    /// <summary>批次级状态 = 所在文件夹。</summary>
    public required BatchLocation Location { get; init; }

    /// <summary>本批次所有行（来自 清单表格.xlsx + manifest）。</summary>
    public List<MaterialRow> Rows { get; init; } = new();

    /// <summary>取数（拉取）时间。</summary>
    public DateTime FetchedAt { get; set; }

    /// <summary>回传时间（已处理批次）。</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>批次键 = 流程 + 窗口起止，唯一。</summary>
    public string Key => $"{Flow}|{WindowStart:yyyyMMddHHmm}-{WindowEnd:yyyyMMddHHmm}";

    public int MaterialCount => Rows.Count;
    public int DoneCount => Rows.Count(r => r.Status is RowStatus.Done or RowStatus.Uploaded);
    public int ExceptionCount => Rows.Count(r => r.Status == RowStatus.Exception);
    public int PendingCount => Rows.Count(r => r.Status == RowStatus.Pending);

    /// <summary>提交闸门：批次内无"待处理"行（每行已处理或已挂起异常）才能整批提交。</summary>
    public bool CanSubmit => Rows.Count > 0 && PendingCount == 0;
}
