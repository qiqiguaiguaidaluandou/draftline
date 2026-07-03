using Draftline.Core.Enums;

namespace Draftline.Core.Models;

/// <summary>
/// 由约定拼出本地路径，以及批次目录名（取数时间范围）的格式化/解析。
/// 批次目录名格式：yyyyMMdd_HHmm-yyyyMMdd_HHmm（同日时末段省略日期，便于阅读）。
/// </summary>
public static class LocalPaths
{
    /// <summary>&lt;根&gt;/&lt;流程&gt;</summary>
    public static string FlowRoot(string root, FlowType flow) =>
        Path.Combine(root, LocalFolders.FlowFolder(flow));

    /// <summary>本地电脑专用：&lt;根&gt;/&lt;流程&gt;/&lt;状态(待处理|已处理)&gt;</summary>
    public static string LocalLocationRoot(string root, FlowType flow, BatchLocation location) =>
        Path.Combine(FlowRoot(root, flow), LocalFolders.LocationFolder(location));

    /// <summary>本地电脑专用：&lt;根&gt;/&lt;流程&gt;/&lt;状态(待处理|已处理)&gt;/&lt;组&gt;</summary>
    public static string LocalGroupRoot(string root, FlowType flow, BatchLocation location, string groupName) =>
        flow == FlowType.DrawingSelection 
            ? LocalLocationRoot(root, flow, location) 
            : Path.Combine(LocalLocationRoot(root, flow, location), groupName);

    /// <summary>服务器端专用：&lt;根&gt;/&lt;流程&gt;/&lt;组&gt; (扁平化，无状态层级)</summary>
    public static string ServerGroupRoot(string root, FlowType flow, string groupName) =>
        flow == FlowType.DrawingSelection
            ? FlowRoot(root, flow)
            : Path.Combine(FlowRoot(root, flow), groupName);

    /// <summary>本地批次目录绝对路径。</summary>
    public static string LocalBatchDir(string root, FlowType flow, BatchLocation location, string groupName, string batchId) =>
        Path.Combine(LocalGroupRoot(root, flow, location, groupName), batchId);

    /// <summary>服务器批次目录绝对路径。</summary>
    public static string ServerBatchDir(string root, FlowType flow, string groupName, string batchId) =>
        Path.Combine(ServerGroupRoot(root, flow, groupName), batchId);

    /// <summary>本地异常池目录。</summary>
    public static string LocalExceptionPoolRoot(string root, FlowType flow) =>
        Path.Combine(FlowRoot(root, flow), LocalFolders.ExceptionPool);

    /// <summary>把窗口起止格式化为批次目录名。</summary>
    public static string BatchFolderName(DateTime start, DateTime end)
    {
        var startPart = start.ToString("yyyyMMdd_HHmm");
        // 同日：末段只留 HHmm，更易读（如 20260520_0931-1530）。
        var endPart = start.Date == end.Date
            ? end.ToString("HHmm")
            : end.ToString("yyyyMMdd_HHmm");
        return $"{startPart}-{endPart}";
    }

    /// <summary>解析批次目录名 → (起,止)。无法解析返回 false。</summary>
    public static bool TryParseFolderName(string folderName, out DateTime start, out DateTime end)
    {
        start = default;
        end = default;
        var dash = folderName.IndexOf('-');
        if (dash <= 0) return false;

        var left = folderName[..dash];
        var right = folderName[(dash + 1)..];

        if (!DateTime.TryParseExact(left, "yyyyMMdd_HHmm", null,
                System.Globalization.DateTimeStyles.None, out start))
            return false;

        if (DateTime.TryParseExact(right, "yyyyMMdd_HHmm", null,
                System.Globalization.DateTimeStyles.None, out end))
            return true;

        // 末段省略了日期（HHmm），沿用起始日期。
        if (TimeOnly.TryParseExact(right, "HHmm", out var endTime))
        {
            end = start.Date.Add(endTime.ToTimeSpan());
            return true;
        }
        return false;
    }
}
