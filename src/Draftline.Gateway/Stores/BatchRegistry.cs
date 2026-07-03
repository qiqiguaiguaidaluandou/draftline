using System.ComponentModel.DataAnnotations;
using Draftline.Core.Enums;

namespace Draftline.Gateway.Stores;

/// <summary>
/// 批次注册表，用于跟踪服务器端所有批次的状态。
/// </summary>
public sealed class BatchRegistry
{
    [Required]
    public string BatchId { get; set; } = ""; // 对应文件夹名

    [Required]
    public string GroupName { get; set; } = "Default";

    public FlowType Flow { get; set; }

    public BatchLocation Status { get; set; } // Todo / Done

    public int TotalRows { get; set; } // 该批次总物料行数，用于快速列表展示

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastModified { get; set; }

    // 提交成功时写入（009）：用于重复提交的幂等回显，避免再次回传外部系统。
    public string? AuditId { get; set; }
}
