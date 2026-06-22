using System.ComponentModel.DataAnnotations;

namespace TZHJ.Gateway.Stores;

/// <summary>
/// 角色：一组数据可见范围的命名捆绑（如"核价员-组1"）。给用户挂角色即授予该范围。
/// 角色只描述"能看哪些数据"——能看见即能操作，不含任何功能级（读/写/提交）区分。
/// </summary>
public sealed class Role
{
    public int Id { get; set; }

    /// <summary>角色名（唯一，如"核价员-组1"）。</summary>
    [Required]
    [MaxLength(64)]
    public string Name { get; set; } = "";

    [MaxLength(256)]
    public string? Description { get; set; }

    public List<RolePermission> Permissions { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
