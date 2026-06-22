using System.ComponentModel.DataAnnotations;

namespace TZHJ.Gateway.Stores;

/// <summary>
/// 系统用户（账号由管理员维护，不接 DHR/SSO）。凭证只存哈希，绝不下发。
/// 权限沿用 <see cref="UserPermission"/>（工号→流程→组）；本表只管"是谁/能不能登录/是不是管理员"。
/// </summary>
public sealed class AppUser
{
    public int Id { get; set; }

    /// <summary>工号（登录账号），唯一。</summary>
    [Required]
    [MaxLength(64)]
    public string EmployeeId { get; set; } = "";

    [Required]
    [MaxLength(64)]
    public string DisplayName { get; set; } = "";

    [MaxLength(64)]
    public string? Department { get; set; }

    [MaxLength(64)]
    public string? Position { get; set; }

    /// <summary>密码哈希（PBKDF2，由 PasswordHasher 产出，自带盐与版本）。</summary>
    [Required]
    public string PasswordHash { get; set; } = "";

    /// <summary>停用后不可登录（保留账号与审计，比删除更安全）。</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>管理员可访问 /api/admin/*（建用户、改密、配权限）。</summary>
    public bool IsAdmin { get; set; }

    /// <summary>初始密码 / 被重置后为真，登录后强制改密。</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>连续登录失败次数（成功或锁定到期后清零）。</summary>
    public int FailedAttempts { get; set; }

    /// <summary>锁定到期 UTC 时间；为空或已过期表示未锁定。</summary>
    public DateTime? LockoutUntil { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
