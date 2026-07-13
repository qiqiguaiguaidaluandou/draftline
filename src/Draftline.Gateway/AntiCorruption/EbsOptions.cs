namespace Draftline.Gateway.AntiCorruption;

/// <summary>
/// EBS 取数接口配置（来自 appsettings 的 "Ebs" 段）。
/// 真实地址/密钥放 appsettings.local.json（不进 git）。URL 留空则取数在运行时报错并按计划重采。
/// </summary>
public sealed class EbsOptions
{
    /// <summary>图纸核价接口 URL（POST）。两个接口地址不同，分别配置。</summary>
    public string PricingUrl { get; set; } = "";

    /// <summary>机加中心挑图接口 URL（POST）。</summary>
    public string DrawingUrl { get; set; } = "";

    /// <summary>生成鉴权 JWT 的密钥（对应对方的 iPaaS_JWT_secret）。</summary>
    public string JwtSecret { get; set; } = "";

    /// <summary>鉴权 JWT 的发行人 iss（对应对方的 iPaaS_JWT_iss）。</summary>
    public string JwtIssuer { get; set; } = "";

    /// <summary>Authorization 头前缀。对方要求带前缀 → "Bearer"。</summary>
    public string AuthScheme { get; set; } = "Bearer";

    /// <summary>图纸核价接口码（固定值）。</summary>
    public string PricingIfaceCode { get; set; } = "CUX_AI_DRW_COST";

    /// <summary>机加中心挑图接口码（固定值）。</summary>
    public string DrawingIfaceCode { get; set; } = "CUX_AI_MACH_DRW";

    /// <summary>机加挑图结果回传接口 URL（POST）。回传 EBS-ID + 是否机加中心可以做。留空则回传在运行时报错、批次不置 Done、可重试。</summary>
    public string DrawingResultUrl { get; set; } = "";

    /// <summary>机加挑图结果回传接口码（固定值，随请求体 P_IFACE_CODE 发送）。</summary>
    public string DrawingResultIfaceCode { get; set; } = "CUX_AI_MACH_DRW_RST";

    /// <summary>
    /// 核价的固定期望组别（完整名，作为文件夹/权限键）。无论某组当天有无数据，都会为每个期望组建批次文件夹+表格，
    /// 空组只是表里没数据行——便于区分"采过但没数据"与"没采到"。
    /// 数据行按"组N"前缀匹配到这里的完整名；匹配不到的（意外组）按其原始组名照常落盘，不丢数据。
    /// </summary>
    /// 默认留空：组名由 appsettings.json 的 Ebs:PricingGroups 提供（单一来源）。
    /// 注意——这里**不能**给非空默认值：.NET 配置绑定数组是“在默认值后追加配置项”，
    /// 若 C# 默认与 appsettings 各给两条，会叠加成四条（组名两两重复）。
    public string[] PricingGroups { get; set; } = Array.Empty<string>();
}
