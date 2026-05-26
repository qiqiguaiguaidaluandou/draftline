using TZHJ.Core.Enums;

namespace TZHJ.Core.Models;

/// <summary>
/// 由约定拼出本地路径，以及批次目录名（取数时间范围）的格式化/解析。
/// 批次目录名格式：yyyyMMdd_HHmm-yyyyMMdd_HHmm（同日时末段省略日期，便于阅读）。
/// </summary>
public static class LocalPaths
{
    /// <summary>&lt;根&gt;/&lt;流程&gt;/&lt;工号&gt;</summary>
    public static string EmployeeRoot(string root, FlowType flow, string employeeId) =>
        Path.Combine(root, LocalFolders.FlowFolder(flow), employeeId);

    /// <summary>&lt;根&gt;/&lt;流程&gt;/&lt;工号&gt;/{待处理|已处理}</summary>
    public static string LocationRoot(string root, FlowType flow, string employeeId, BatchLocation location) =>
        Path.Combine(EmployeeRoot(root, flow, employeeId), LocalFolders.LocationFolder(location));

    /// <summary>&lt;根&gt;/&lt;流程&gt;/&lt;工号&gt;/异常待跟进</summary>
    public static string ExceptionPoolRoot(string root, FlowType flow, string employeeId) =>
        Path.Combine(EmployeeRoot(root, flow, employeeId), LocalFolders.ExceptionPool);

    /// <summary>批次目录绝对路径。</summary>
    public static string BatchDir(string root, FlowType flow, string employeeId, BatchLocation location, string folderName) =>
        Path.Combine(LocationRoot(root, flow, employeeId, location), folderName);

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
