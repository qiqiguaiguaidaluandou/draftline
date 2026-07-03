using System.ComponentModel.DataAnnotations;
using Draftline.Core.Enums;

namespace Draftline.Gateway.Stores;

/// <summary>
/// 统一活动日志：整合业务审计、用户行为和系统任务。
/// </summary>
public sealed class ActivityLog
{
    [Key]
    public long Id { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [Required]
    public string EmployeeId { get; set; } = "";

    [Required]
    public string Action { get; set; } = ""; // Login, Submit, UpdateRow, Suspend, Resolve, Ingest, Cleanup

    public FlowType? Flow { get; set; }

    public string? GroupName { get; set; }

    public string? BatchId { get; set; }

    public int ImpactCount { get; set; } // 受影响行数

    public string Status { get; set; } = "Success"; // Success, Failed

    public string? Payload { get; set; } // JSON 格式的具体信息

    public string? ClientIp { get; set; }

    // 结构化审计字段（009）：回传相关动作（Action="Submit"）填充，供补拉判据精确查询，
    // 取代原先在 Payload 里塞文本再字符串反解析的脆弱做法。
    public DateTime? WindowStart { get; set; }

    public DateTime? WindowEnd { get; set; }

    public string? AuditId { get; set; }
}
