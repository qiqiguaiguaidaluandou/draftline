using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Draftline.Core.Enums;

namespace Draftline.Gateway.Stores;

/// <summary>用户操作日志实体（= PostgreSQL operation_logs 表的行）。</summary>
[Table("operation_logs")]
public sealed class OperationLogEntity
{
    [Key]
    [Column("log_id")]
    public long LogId { get; init; }

    [Column("employee_id")]
    [MaxLength(50)]
    public required string EmployeeId { get; init; }

    [Column("operation")]
    [MaxLength(100)]
    public required string Operation { get; init; }

    [Column("form_name")]
    [MaxLength(200)]
    public required string FormName { get; init; }

    [Column("flow")]
    public required FlowType Flow { get; init; }

    [Column("client_ip")]
    [MaxLength(50)]
    public required string ClientIp { get; init; }

    [Column("operated_at")]
    public required DateTime OperatedAt { get; init; }
}
