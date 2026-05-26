namespace TZHJ.Infrastructure.Options;

/// <summary>
/// 本地存储根目录（可变）。客户端登录后用下发配置 ClientConfig.LocalRoot 设置；样例生成器用 CLI 指定。
/// 与 Mock 解耦：真实网关上线后存储层不变，仍读这里的 Root。
/// </summary>
public sealed class LocalStorageOptions
{
    public string Root { get; set; } = "TZHJ_Data";
}
