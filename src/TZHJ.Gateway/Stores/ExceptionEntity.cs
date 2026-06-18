using System.ComponentModel.DataAnnotations;
using TZHJ.Core.Enums;

namespace TZHJ.Gateway.Stores;

/// <summary>
/// 云端异常池实体。
/// </summary>
public sealed class ExceptionEntity
{
    public int Id { get; set; }

    [Required]
    public string GroupName { get; set; } = "Default";

    public FlowType Flow { get; set; }

    [Required]
    public string RowKey { get; set; } = "";

    public string MaterialCode { get; set; } = "";

    public string? DisplayName { get; set; }

    [Required]
    public string SourceBatch { get; set; } = "";

    [Required]
    public string Reason { get; set; } = "";

    public RowStatus Status { get; set; } = RowStatus.Exception; // Exception (Open) or Uploaded (Resolved)

    public DateTime SuspendedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public string? ResolvedBy { get; set; }

    public string? ResolutionAuditId { get; set; } 
}
