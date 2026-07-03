namespace Draftline.Gateway.Auth;

/// <summary>JWT 签发/校验配置（来自 appsettings 的 "Jwt" 段；密钥放 appsettings.local.json，不入库）。</summary>
public sealed class JwtOptions
{
    /// <summary>HMAC-SHA256 对称密钥。至少 32 字节（256 位）。生产必须改成强随机值。</summary>
    public string Key { get; set; } = "";

    public string Issuer { get; set; } = "Draftline.Gateway";

    public string Audience { get; set; } = "Draftline.App";

    /// <summary>令牌有效期（分钟）。</summary>
    public int ExpiryMinutes { get; set; } = 480;
}
