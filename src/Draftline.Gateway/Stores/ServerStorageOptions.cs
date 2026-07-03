namespace Draftline.Gateway.Stores;

/// <summary>
/// 服务器端文件存储选项。
/// </summary>
public sealed class ServerStorageOptions
{
    /// <summary>
    /// 服务器端批次数据的根目录路径（Source of Truth）。
    /// </summary>
    public string ServerRoot { get; set; } = "App_Data/ServerStorage";
}
