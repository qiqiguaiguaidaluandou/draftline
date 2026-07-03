using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Draftline.Gateway.Auth;

/// <summary>
/// 会话令牌服务：签发/校验带签名(HMAC-SHA256)与过期的 JWT，工号置于 sub。
/// 令牌不携带凭证；服务端按工号实时查库取身份/权限（demote 即时生效）。
/// </summary>
public interface ITokenService
{
    string Issue(string employeeId);

    /// <summary>校验令牌（签名 + 过期 + 颁发者/受众）并取出工号；无效返回 null。</summary>
    string? ResolveEmployeeId(string? token);
}

/// <summary>真实 JWT 令牌服务。密钥/有效期来自 <see cref="JwtOptions"/>。</summary>
public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _opt;
    private readonly SymmetricSecurityKey _key;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public JwtTokenService(JwtOptions opt)
    {
        _opt = opt;
        if (string.IsNullOrWhiteSpace(opt.Key) || Encoding.UTF8.GetByteCount(opt.Key) < 32)
            throw new InvalidOperationException("Jwt:Key 缺失或长度不足 32 字节，请在 appsettings.local.json 配置强随机密钥。");
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.Key));
    }

    public string Issue(string employeeId)
    {
        var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, employeeId) },
            notBefore: now,
            expires: now.AddMinutes(_opt.ExpiryMinutes),
            signingCredentials: creds);
        return _handler.WriteToken(token);
    }

    public string? ResolveEmployeeId(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _key,
            ValidateIssuer = true,
            ValidIssuer = _opt.Issuer,
            ValidateAudience = true,
            ValidAudience = _opt.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
        try
        {
            var principal = _handler.ValidateToken(token, parameters, out _);
            return principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                   ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        }
        catch
        {
            return null;
        }
    }
}
