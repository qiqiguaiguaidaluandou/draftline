using System.ComponentModel.DataAnnotations;
using TZHJ.Core.Enums;

namespace TZHJ.Gateway.Stores;

/// <summary>
/// 用户权限实体，映射工号到特定的流程和业务组。
/// </summary>
public sealed class UserPermission
{
    public int Id { get; set; }

    [Required]
    public string EmployeeId { get; set; } = "";

    public FlowType Flow { get; set; }

    /// <summary>
    /// 授权的组名。"*" 代表该流程下的所有组。
    /// </summary>
    [Required]
    public string GroupName { get; set; } = "";
}
