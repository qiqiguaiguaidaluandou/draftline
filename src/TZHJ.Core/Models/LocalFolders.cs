using TZHJ.Core.Enums;

namespace TZHJ.Core.Models;

/// <summary>
/// 本地存储约定（文件夹/文件命名）。软件视图 = 本地文件夹视图，故命名是契约的一部分。
/// 结构：&lt;本地根&gt;/{核价|挑图}/&lt;工号&gt;/{待处理|已处理|异常待跟进}/&lt;取数时间范围&gt;/
/// </summary>
public static class LocalFolders
{
    public const string Pricing = "核价";
    public const string DrawingSelection = "挑图";

    public const string Todo = "待处理";
    public const string Done = "已处理";
    public const string ExceptionPool = "异常待跟进";

    /// <summary>批次目录内的表格文件名（料号 + 只读字段 + 待填列）。</summary>
    public const string GridWorkbook = "清单表格.xlsx";

    /// <summary>批次目录内的 sidecar：行级状态/异常原因/来源/取数时间等（manifest）。</summary>
    public const string Manifest = "_manifest.json";

    /// <summary>异常待跟进池的汇总文件。</summary>
    public const string ExceptionPoolFile = "_异常池.json";

    public static string FlowFolder(FlowType flow) =>
        flow == FlowType.Pricing ? Pricing : DrawingSelection;

    public static string LocationFolder(BatchLocation location) =>
        location == BatchLocation.Todo ? Todo : Done;
}
