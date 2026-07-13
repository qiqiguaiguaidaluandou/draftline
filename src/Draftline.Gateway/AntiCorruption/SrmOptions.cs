namespace Draftline.Gateway.AntiCorruption;

/// <summary>
/// SRM 价格回传接口配置（来自 appsettings 的 "Srm" 段），仅核价→SRM 这一支用。鉴权复用 EBS 的 JWT
/// （同 Ebs:JwtSecret/JwtIssuer/AuthScheme）。真实地址放 appsettings.local.json（不进 git）。
/// 挑图→EBS 机加结果回传的地址/接口码在 Ebs 段（DrawingResultUrl/DrawingResultIfaceCode）。
/// </summary>
public sealed class SrmOptions
{
    /// <summary>价格回传接口 URL（POST）。</summary>
    public string Url { get; set; } = "";

    /// <summary>固定的接口码，随请求体发送。</summary>
    public string InterfaceCode { get; set; } = "AI_ITEM_PRICE_TO_SRM";
}
