using Microsoft.AspNetCore.Identity;
using TZHJ.Gateway.Stores;

namespace TZHJ.Gateway.Auth;

/// <summary>密码哈希/校验。封装 ASP.NET Core 的 <see cref="PasswordHasher{TUser}"/>（PBKDF2-HMAC-SHA256，自带随机盐与算法版本号）。</summary>
public interface IPasswordService
{
    string Hash(string password);

    /// <summary>校验明文是否匹配哈希。</summary>
    bool Verify(string hash, string password);
}

public sealed class PasswordService : IPasswordService
{
    // PasswordHasher 不依赖 TUser 实例内容，传 null! 安全。
    private readonly PasswordHasher<AppUser> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public bool Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        var result = _hasher.VerifyHashedPassword(null!, hash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
