namespace Draftline.Infrastructure.Options;

/// <summary>
/// 纯客户端、按机器持久化的本机设置（存 %AppData%\Draftline\client-settings.json）。
/// 与后端下发的 ClientConfig 无关：本地数据根是本机概念，后端不该替客户端指定物理路径。
/// 存 %AppData% 而非程序目录：ClickOnce 程序目录带版本号、每次更新即换，写在那里会丢。
/// </summary>
public sealed class ClientSettings
{
    /// <summary>用户选定的本地数据根目录；为空表示沿用默认（我的文档\Draftline_Data）。</summary>
    public string? LocalRoot { get; set; }

    /// <summary>
    /// 待迁移来源：更改路径时置为旧根，下次启动在任何组件用到路径之前把它整体迁到 LocalRoot 再清空。
    /// 放到启动早期做，避开同步调度器/文件句柄，迁移最安全。
    /// </summary>
    public string? PendingMoveFrom { get; set; }
}
