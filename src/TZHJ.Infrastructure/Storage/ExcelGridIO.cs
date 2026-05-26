using ClosedXML.Excel;
using TZHJ.Core.Models;

namespace TZHJ.Infrastructure.Storage;

/// <summary>
/// 清单表格.xlsx 的读写。表格本体就是这个 xlsx：表头 = 字段显示名，每行 = 一个料号的字段值。
/// 行状态不进 xlsx（在 manifest），保持 xlsx 纯字段、可被操作员只读打开。列由字段 schema 驱动。
/// </summary>
public static class ExcelGridIO
{
    private const string SheetName = "清单表格";

    public static void Write(string path, IReadOnlyList<FieldDefinition> fields, IEnumerable<MaterialRow> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(SheetName);

        var ordered = fields.OrderBy(f => f.Order).ToList();

        // 表头
        for (var c = 0; c < ordered.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = ordered[c].DisplayName;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FAFBFC");
        }

        // 数据行
        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < ordered.Count; c++)
                ws.Cell(r, c + 1).Value = row.Get(ordered[c].Key) ?? string.Empty;
            r++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }

    /// <summary>读 xlsx → 行（rowKey + 字段值字典）。表头按显示名映射回字段键。</summary>
    public static List<(string RowKey, Dictionary<string, string?> Values)> Read(
        string path, IReadOnlyList<FieldDefinition> fields)
    {
        var result = new List<(string, Dictionary<string, string?>)>();
        if (!File.Exists(path)) return result;

        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();

        // 表头：显示名 → 列号
        var headerMap = new Dictionary<string, int>();
        foreach (var cell in ws.Row(1).CellsUsed())
            headerMap[cell.GetString().Trim()] = cell.Address.ColumnNumber;

        // 字段键 → 列号（经显示名）
        var keyToCol = fields
            .Where(f => headerMap.ContainsKey(f.DisplayName))
            .ToDictionary(f => f.Key, f => headerMap[f.DisplayName]);

        var rowKeyField = fields.FirstOrDefault(f => f.IsRowKey);
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (var r = 2; r <= lastRow; r++)
        {
            var values = new Dictionary<string, string?>();
            var anyValue = false;
            foreach (var (key, col) in keyToCol)
            {
                var v = ws.Cell(r, col).GetString();
                values[key] = string.IsNullOrEmpty(v) ? null : v;
                if (!string.IsNullOrEmpty(v)) anyValue = true;
            }
            if (!anyValue) continue; // 跳过空行

            var rowKey = rowKeyField != null ? values.GetValueOrDefault(rowKeyField.Key) ?? "" : "";
            result.Add((rowKey, values));
        }

        return result;
    }
}
