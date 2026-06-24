namespace TZHJ.Gateway.AntiCorruption;

/// <summary>
/// SRM 回传接口配置（来自 appsettings 的 "Srm" 段）。鉴权复用 EBS 的 JWT（同 Ebs:JwtSecret/JwtIssuer/AuthScheme）。
/// Enabled=false（默认）时回传仍由 FakeDataSource 顶替。真实地址放 appsettings.local.json（不进 git）。
/// 仅图纸核价→SRM 这一支用真实接口；机加挑图→EBS 回传接口尚未提供，继续走 Fake。
/// </summary>
public sealed class SrmOptions
{
    /// <summary>是否启用真实 SRM 回传。false（默认）→ 继续用 FakeDataSource。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>价格回传接口 URL（POST）。</summary>
    public string Url { get; set; } = "";

    /// <summary>固定的接口码，随请求体发送。</summary>
    public string InterfaceCode { get; set; } = "AI_ITEM_PRICE_TO_SRM";
}
