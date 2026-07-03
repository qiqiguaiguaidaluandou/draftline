namespace Draftline.Gateway.AntiCorruption;

/// <summary>
/// PLM 取数接口配置（来自 appsettings 的 "Plm" 段）。
/// 鉴权复用 EBS 的 JWT（同 Ebs:JwtSecret/JwtIssuer/AuthScheme），不单独配密钥。
/// 任一 URL 留空 → 对应富化步骤被跳过（便于在 PLM 地址未就绪时先单独验证 EBS）。
/// 真实地址放 appsettings.local.json（不进 git）。
/// </summary>
public sealed class PlmOptions
{
    /// <summary>变更状态接口 URL（POST 物料编码数组 → data[]{itemNumber, isChange}）。空 → 跳过变更富化。</summary>
    public string ChangeUrl { get; set; } = "";

    /// <summary>图纸附件接口 URL（POST 物料编码数组 → data[]{itemNumber, files[]{fileName, fileStr}}）。空 → 跳过图纸下载。</summary>
    public string DrawingUrl { get; set; } = "";

    /// <summary>单次请求携带的物料编码数量上限，超出按此大小分多批调用。</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>单个图纸文件下载大小上限（字节），超限跳过该文件并告警。默认 50MB。</summary>
    public long MaxDrawingBytes { get; set; } = 50L * 1024 * 1024;
}
