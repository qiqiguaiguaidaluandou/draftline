using System.ComponentModel.DataAnnotations;

namespace Draftline.Gateway.Stores;

/// <summary>用户↔角色 指派（多对多）。一个用户可挂多个角色，有效可见范围 = 各角色范围并集。</summary>
public sealed class UserRole
{
    public int Id { get; set; }

    [Required]
    [MaxLength(64)]
    public string EmployeeId { get; set; } = "";

    public int RoleId { get; set; }
    public Role? Role { get; set; }
}
