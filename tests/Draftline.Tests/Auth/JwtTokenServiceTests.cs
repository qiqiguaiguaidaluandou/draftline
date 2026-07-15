using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Draftline.Gateway.Auth;

namespace Draftline.Tests.Auth;

public class JwtTokenServiceTests
{
    private static JwtOptions Opt(string key = "unit-test-signing-key-0123456789-abcdef", int expiry = 480) =>
        new() { Key = key, Issuer = "Draftline.Gateway", Audience = "Draftline.App", ExpiryMinutes = expiry };

    [Fact]
    public void Issue_then_Resolve_roundtrips_employee_id()
    {
        var svc = new JwtTokenService(Opt());
        var token = svc.Issue("10086");
        Assert.Equal("10086", svc.ResolveEmployeeId(token));
    }

    [Fact]
    public void Resolve_null_or_blank_returns_null()
    {
        var svc = new JwtTokenService(Opt());
        Assert.Null(svc.ResolveEmployeeId(null));
        Assert.Null(svc.ResolveEmployeeId(""));
        Assert.Null(svc.ResolveEmployeeId("not-a-jwt"));
    }

    [Fact]
    public void Tampered_token_is_rejected()
    {
        var svc = new JwtTokenService(Opt());
        var token = svc.Issue("10086");
        // 篡改签名段：翻转其「首字符」。base64url 中末位字符可能只携带填充位——翻转它
        // 有几率解码出同一串字节、签名依旧有效（此前偶发假失败的根因）；而任何非末位字符
        // 都落在完整字节内，翻转必改变解码后的签名字节，保证令牌确实失效。
        var parts = token.Split('.');
        var sig = parts[2];
        parts[2] = (sig[0] == 'A' ? 'B' : 'A') + sig[1..];
        var tampered = string.Join('.', parts);
        Assert.Null(svc.ResolveEmployeeId(tampered));
    }

    [Fact]
    public void Token_signed_with_other_key_is_rejected()
    {
        var issuer = new JwtTokenService(Opt("issuer-key-aaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
        var verifier = new JwtTokenService(Opt("verifier-key-bbbbbbbbbbbbbbbbbbbbbbbbbbbb"));
        var token = issuer.Issue("10086");
        Assert.Null(verifier.ResolveEmployeeId(token));
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        var opt = Opt();
        // 手造一枚已过期但构造合法的令牌（nbf 20 分钟前、exp 10 分钟前，超出 1 分钟时钟偏移容忍）。
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.Key));
        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken(
            issuer: opt.Issuer,
            audience: opt.Audience,
            claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, "10086") },
            notBefore: now.AddMinutes(-20),
            expires: now.AddMinutes(-10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);

        Assert.Null(new JwtTokenService(opt).ResolveEmployeeId(token));
    }

    [Fact]
    public void Short_key_is_rejected_at_construction()
    {
        Assert.Throws<InvalidOperationException>(() => new JwtTokenService(Opt(key: "too-short")));
        Assert.Throws<InvalidOperationException>(() => new JwtTokenService(Opt(key: "")));
    }
}
