using System.Text;
using TZHJ.Core.Contracts;
using TZHJ.Core.Enums;
using TZHJ.Core.Schemas;
using TZHJ.Infrastructure.Options;

namespace TZHJ.Infrastructure.Gateways.Mock;

/// <summary>
/// 造数网关：按字段 schema 生成 EBS+PLM 只读值，生成占位 PDF/STEP 图纸字节；
/// 待填列（核价目标价 / 挑图是否机加中心可以做）留空待操作员填。
/// 同一 (流程+窗口) 确定性生成（种子相关），重复取数得同一批数据。
/// 真接入后替换为 HttpDataGateway（带工号调 EBS + 按物料编码调 PLM）。
/// </summary>
public sealed class MockDataGateway : IDataGateway
{
    private static readonly string[] PricingNames = { "支架", "法兰盘", "轴套", "盖板", "连接件", "端盖", "导轨", "压板", "齿条", "销轴" };
    private static readonly string[] PricingSpecs = { "Q235 3mm", "45# 调质", "不锈钢304", "铝6061", "Q355", "铸铁 HT250", "20CrMnTi", "黄铜H62" };
    private static readonly string[] DrawingNames = { "异形件", "薄壁壳体", "支撑座", "齿轮箱体", "主轴", "泵体", "阀座", "联轴器", "导向块", "法兰" };
    private static readonly string[] DrawingMats = { "钛合金", "铝合金", "铸钢", "铸铝", "40Cr", "铸铁", "304不锈钢", "QT600" };
    private static readonly string[] ProductLines = { "产品线A", "产品线B", "产品线C" };
    private static readonly string[] Departments = { "机加一车间", "机加二车间", "钣金车间" };
    private static readonly string[] Applicants = { "王工", "赵工", "钱工", "孙工" };
    private static readonly string[] ChangeStates = { "无变更", "无变更", "无变更", "有变更" }; // 多数无变更

    private readonly MockOptions _options;

    public MockDataGateway(MockOptions options) => _options = options;

    public async Task<FetchResult> FetchBatchAsync(FetchRequest request, CancellationToken ct = default)
    {
        await Task.Delay(_options.FetchDelayMs, ct);

        // 确定性种子：同一工号+流程+窗口 → 同一批数据。
        var seed = _options.Seed
                   ^ request.EmployeeId.GetHashCode()
                   ^ (int)request.Flow
                   ^ request.WindowStart.GetHashCode()
                   ^ request.WindowEnd.GetHashCode();
        var rng = new Random(seed);

        var count = rng.Next(_options.MinRowsPerBatch, _options.MaxRowsPerBatch + 1);
        var rows = new List<FetchedRow>(count);

        for (var i = 0; i < count; i++)
        {
            var row = request.Flow == FlowType.Pricing
                ? BuildPricingRow(rng, i)
                : BuildDrawingRow(rng, request.WindowStart, i);
            rows.Add(row);
        }

        return new FetchResult
        {
            Success = true,
            Flow = request.Flow,
            EmployeeId = request.EmployeeId,
            WindowStart = request.WindowStart,
            WindowEnd = request.WindowEnd,
            Rows = rows,
        };
    }

    private FetchedRow BuildPricingRow(Random rng, int i)
    {
        var code = $"M-{10000 + rng.Next(200, 999)}"; // 形如 M-10231
        var name = Pick(rng, PricingNames) + (char)('A' + rng.Next(0, 5));
        var spec = Pick(rng, PricingSpecs);

        var values = new Dictionary<string, string?>
        {
            [FieldSchemas.PricingKeys.MaterialCode] = code,
            [FieldSchemas.PricingKeys.Model] = $"GB-{rng.Next(1000, 9999)}",
            [FieldSchemas.PricingKeys.Name] = $"{name} / {spec}",
            [FieldSchemas.PricingKeys.DemandQty] = rng.Next(1, 500).ToString(),
            [FieldSchemas.PricingKeys.HasChange] = Pick(rng, ChangeStates),
            [FieldSchemas.PricingKeys.TargetPrice] = null, // 待填列：操作员手填
        };

        return new FetchedRow
        {
            RowKey = code,
            Values = values,
            Drawings = BuildDrawings(rng, code, name),
        };
    }

    private FetchedRow BuildDrawingRow(Random rng, DateTime windowStart, int i)
    {
        var code = $"P-{2000 + rng.Next(1, 99)}";
        var name = Pick(rng, DrawingNames);
        var mat = Pick(rng, DrawingMats);
        var ebsId = $"EBS-{windowStart:yyyyMMdd}-{rng.Next(1, 9999):D4}";

        var values = new Dictionary<string, string?>
        {
            [FieldSchemas.DrawingKeys.EbsId] = ebsId,
            [FieldSchemas.DrawingKeys.InvOrg] = rng.Next(2) == 0 ? "本部" : "分厂",
            [FieldSchemas.DrawingKeys.SourceNo] = $"PO-{rng.Next(100000, 999999)}",
            [FieldSchemas.DrawingKeys.Project] = $"项目{(char)('A' + rng.Next(0, 4))}",
            [FieldSchemas.DrawingKeys.ProductLine] = Pick(rng, ProductLines),
            [FieldSchemas.DrawingKeys.PlanNo] = $"FA-{rng.Next(1000, 9999)}",
            [FieldSchemas.DrawingKeys.DeptDesc] = Pick(rng, Departments),
            [FieldSchemas.DrawingKeys.MaterialCode] = code,
            [FieldSchemas.DrawingKeys.MaterialDesc] = $"{name} / {mat}",
            [FieldSchemas.DrawingKeys.CurrentQty] = rng.Next(1, 200).ToString(),
            [FieldSchemas.DrawingKeys.CreateDate] = windowStart.AddDays(-rng.Next(1, 5)).ToString("yyyy-MM-dd"),
            [FieldSchemas.DrawingKeys.DemandDate] = windowStart.AddDays(rng.Next(7, 30)).ToString("yyyy-MM-dd"),
            [FieldSchemas.DrawingKeys.Applicant] = Pick(rng, Applicants),
            [FieldSchemas.DrawingKeys.Remark] = rng.Next(4) == 0 ? "加急" : "",
            [FieldSchemas.DrawingKeys.HasChange] = Pick(rng, ChangeStates),
            [FieldSchemas.DrawingKeys.CanMachine] = null, // 待填列：操作员手填（是/否）
        };

        return new FetchedRow
        {
            RowKey = ebsId,
            Values = values,
            Drawings = BuildDrawings(rng, code, name),
        };
    }

    /// <summary>生成该料号的图纸（pdf+step）。按概率返回空列表，模拟"图纸缺失"。</summary>
    private List<FetchedDrawing> BuildDrawings(Random rng, string materialCode, string name)
    {
        var list = new List<FetchedDrawing>();
        if (rng.NextDouble() < _options.DrawingMissingRate)
            return list; // 无图纸 → UI 标"缺失"，操作员可挂起异常

        var safeName = MakeSafe(name);
        list.Add(new FetchedDrawing
        {
            FileName = $"{materialCode}__{safeName}.pdf",
            MaterialCode = materialCode,
            Content = MakePlaceholderPdf($"{materialCode}  (MOCK DRAWING)"),
        });
        list.Add(new FetchedDrawing
        {
            FileName = $"{materialCode}__{safeName}.step",
            MaterialCode = materialCode,
            Content = MakePlaceholderStep(materialCode),
        });
        return list;
    }

    private static string Pick(Random rng, string[] pool) => pool[rng.Next(pool.Length)];

    private static string MakeSafe(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    /// <summary>构造一个结构最简的合法单页 PDF（操作员双击能打开看到一行文字）。文字保持 ASCII。</summary>
    private static byte[] MakePlaceholderPdf(string asciiText)
    {
        var sb = new StringBuilder();
        var offsets = new List<int>();
        void Obj(string body) { offsets.Add(sb.Length); sb.Append(body); }

        sb.Append("%PDF-1.4\n");
        Obj("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Obj("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        Obj("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 420 200] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n");
        var content = $"BT /F1 16 Tf 36 150 Td ({Escape(asciiText)}) Tj ET";
        Obj($"4 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");
        Obj("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        var xref = sb.Length;
        sb.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var off in offsets) sb.Append(off.ToString("D10")).Append(" 00000 n \n");
        sb.Append($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static string Escape(string s) => s.Replace("(", "\\(").Replace(")", "\\)");

    /// <summary>构造一个最简 STEP(ISO-10303-21) 文本占位文件。</summary>
    private static byte[] MakePlaceholderStep(string code)
    {
        var step = "ISO-10303-21;\nHEADER;\n" +
                   $"FILE_DESCRIPTION(('TZHJ MOCK STEP {code}'),'2;1');\n" +
                   $"FILE_NAME('{code}.step','2026-05-25T00:00:00',(''),(''),'mock','TZHJ','');\n" +
                   "FILE_SCHEMA(('AUTOMOTIVE_DESIGN'));\nENDSEC;\nDATA;\nENDSEC;\nEND-ISO-10303-21;\n";
        return Encoding.UTF8.GetBytes(step);
    }
}
