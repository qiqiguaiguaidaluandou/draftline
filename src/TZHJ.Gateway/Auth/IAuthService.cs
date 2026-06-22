using Microsoft.EntityFrameworkCore;
using TZHJ.Core.Contracts;
using TZHJ.Core.Enums;
using TZHJ.Core.Models;
using TZHJ.Gateway.Stores;

namespace TZHJ.Gateway.Auth;

/// <summary>认证：校验账号/密码（本地凭证）→ 组装身份与权限 → 签发令牌。含失败计数与锁定。</summary>
public interface IAuthService
{
    Task<AuthResult> LoginAsync(string employeeId, string password, CancellationToken ct = default);

    /// <summary>本人改密：校验旧密码后写新哈希，清除强制改密标志。</summary>
    Task<(bool Ok, string Message)> ChangePasswordAsync(string employeeId, string oldPassword, string newPassword, CancellationToken ct = default);
}

/// <summary>
/// 本地凭证认证：账号存 <see cref="AppUser"/>（管理员维护），密码存 PBKDF2 哈希。
/// 连续失败 <see cref="MaxFailedAttempts"/> 次锁定 <see cref="LockoutMinutes"/> 分钟，防爆破。
/// </summary>
public sealed class DbAuthService : IAuthService
{
    public const int MaxFailedAttempts = 5;
    public const int LockoutMinutes = 15;
    public const int MinPasswordLength = 8;

    private static readonly AuthResult InvalidCredentials = AuthResult.Fail("工号或密码错误。");

    private readonly TzhjDbContext _db;
    private readonly IPasswordService _passwords;
    private readonly ITokenService _tokens;

    public DbAuthService(TzhjDbContext db, IPasswordService passwords, ITokenService tokens)
    {
        _db = db;
        _passwords = passwords;
        _tokens = tokens;
    }

    public async Task<AuthResult> LoginAsync(string employeeId, string password, CancellationToken ct = default)
    {
        employeeId = employeeId?.Trim() ?? "";
        if (employeeId.Length == 0 || string.IsNullOrEmpty(password))
            return AuthResult.Fail("工号和密码不能为空。");

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.EmployeeId == employeeId, ct);

        // 账号不存在：返回与"密码错误"一致的提示，避免工号枚举。
        if (user is null)
            return InvalidCredentials;

        var now = DateTime.UtcNow;
        if (user.LockoutUntil is { } until && until > now)
        {
            var mins = Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes));
            return AuthResult.Fail($"账号已锁定，请 {mins} 分钟后再试。");
        }

        if (!user.IsActive)
            return AuthResult.Fail("账号已停用，请联系管理员。");

        if (!_passwords.Verify(user.PasswordHash, password))
        {
            user.FailedAttempts++;
            if (user.FailedAttempts >= MaxFailedAttempts)
            {
                user.LockoutUntil = now.AddMinutes(LockoutMinutes);
                user.FailedAttempts = 0;
            }
            user.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            return InvalidCredentials;
        }

        // 成功：清失败计数/锁定。
        user.FailedAttempts = 0;
        user.LockoutUntil = null;
        user.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        var flows = await _db.UserPermissions
            .Where(p => p.EmployeeId == employeeId)
            .Select(p => p.Flow)
            .Distinct()
            .ToListAsync(ct);

        var identity = new OperatorIdentity
        {
            EmployeeId = user.EmployeeId,
            DisplayName = user.DisplayName,
            Department = user.Department,
            Position = user.Position,
            AllowedFlows = flows,
            CanOperate = true,
            CanSubmit = true,
        };

        return new AuthResult
        {
            Success = true,
            Operator = identity,
            Token = _tokens.Issue(user.EmployeeId),
            MustChangePassword = user.MustChangePassword,
        };
    }

    public async Task<(bool Ok, string Message)> ChangePasswordAsync(string employeeId, string oldPassword, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(oldPassword) || string.IsNullOrEmpty(newPassword))
            return (false, "旧密码和新密码不能为空。");
        if (newPassword.Length < MinPasswordLength)
            return (false, $"新密码长度至少 {MinPasswordLength} 位。");
        if (newPassword == oldPassword)
            return (false, "新密码不能与旧密码相同。");

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.EmployeeId == employeeId, ct);
        if (user is null) return (false, "账号不存在。");
        if (!user.IsActive) return (false, "账号已停用，请联系管理员。");
        if (!_passwords.Verify(user.PasswordHash, oldPassword))
            return (false, "当前密码不正确。");

        user.PasswordHash = _passwords.Hash(newPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return (true, "密码已更新。");
    }
}
