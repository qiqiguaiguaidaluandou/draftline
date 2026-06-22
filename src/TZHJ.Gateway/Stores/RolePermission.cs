using System.ComponentModel.DataAnnotations;
using TZHJ.Core.Enums;

namespace TZHJ.Gateway.Stores;

/// <summary>角色含的一条数据范围：某流程的某组。GroupName="*" 代表该流程下全部组。</summary>
public sealed class RolePermission
{
    public int Id { get; set; }

    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public FlowType Flow { get; set; }

    [Required]
    [MaxLength(64)]
    public string GroupName { get; set; } = "";
}
