using System.IO;
using Draftline.Core.Enums;

namespace Draftline.Core.Models;

/// <summary>
/// 本地存储约定（文件夹/文件命名）。软件视图 = 本地文件夹视图，故命名是契约的一部分。
/// 结构：&lt;本地根&gt;/{核价|挑图}/&lt;工号&gt;/{待处理|已处理|异常待跟进}/&lt;取数时间范围&gt;/
/// </summary>
public static class LocalFolders
{
    public const string Pricing = "图纸核价";
    public const string DrawingSelection = "机加中心挑图";

    public const string Todo = "待处理";
    public const string Done = "已处理";
    public const string ExceptionPool = "异常待跟进";

    /// <summary>
    /// 批次目录内表格文件名：以作业流程名（如「图纸核价」）作前缀 + 批次数据范围（取数时间窗），
    /// 便于文件离开目录后仍能一眼看出归属哪个流程与数据范围。如「图纸核价_20260519_1531-20260520_0930.xlsx」。
    /// </summary>
    public static string GridWorkbookName(FlowType flow, string batchFolderName)
        => $"{FlowFolder(flow)}_{batchFolderName}.xlsx";

    /// <summary>历史命名（无流程前缀，与批次目录同名），仅用于兼容读取旧批次。</summary>
    public static string LegacyGridWorkbookName(string batchFolderName) => batchFolderName + ".xlsx";

    /// <summary>
    /// 解析批次目录里的表格路径（读取用）：优先新命名（含流程前缀），旧批次回退无前缀命名；
    /// 两者都不存在时返回新命名路径。只读判断、不改磁盘。
    /// </summary>
    public static string ResolveGridWorkbookPath(string batchDir, FlowType flow, string batchFolderName)
    {
        var preferred = Path.Combine(batchDir, GridWorkbookName(flow, batchFolderName));
        if (File.Exists(preferred)) return preferred;
        var legacy = Path.Combine(batchDir, LegacyGridWorkbookName(batchFolderName));
        return File.Exists(legacy) ? legacy : preferred;
    }

    /// <summary>批次目录内的 sidecar：行级状态/异常原因/来源/取数时间等（manifest）。</summary>
    public const string Manifest = "_manifest.json";

    /// <summary>异常待跟进池的汇总文件。</summary>
    public const string ExceptionPoolFile = "_异常池.json";

    public static string FlowFolder(FlowType flow) =>
        flow == FlowType.Pricing ? Pricing : DrawingSelection;

    public static string LocationFolder(BatchLocation location) =>
        location == BatchLocation.Todo ? Todo : Done;
}
