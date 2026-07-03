namespace Draftline.Gateway.ClickOnce;

/// <summary>
/// 客户端 ClickOnce 发布物的托管选项（对应 appsettings 的 "ClickOnce" 段）。
/// 让后端直接对外发布 setup.exe / .application / Application Files，使客户端经同一 host/端口/域名
/// 安装与自动更新，无需另装 nginx。部署见 docs/开发文档-⑨。
/// </summary>
public sealed class ClickOnceOptions
{
    /// <summary>
    /// 发布物存放目录：把 Windows 上 `dotnet publish` 出的 publish\ 内容整体拷进来。
    /// 相对路径基于内容根目录（如 clickonce → {ContentRoot}/clickonce）。
    /// </summary>
    public string DistPath { get; set; } = "clickonce";

    /// <summary>
    /// 对外访问前缀。客户端从 {host}{RequestPath}/setup.exe 安装、并据此自动更新。
    /// 须与 ClickOnceProfile.pubxml 的 InstallUrl/PublishUrl 路径一致。
    /// </summary>
    public string RequestPath { get; set; } = "/draftline";
}
