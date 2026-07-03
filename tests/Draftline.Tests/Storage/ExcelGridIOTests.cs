using Draftline.Core.Models;
using Draftline.Core.Schemas;
using Draftline.Infrastructure.Storage;

namespace Draftline.Tests.Storage;

public class ExcelGridIOTests : IDisposable
{
    private readonly string _dir = TempDir.Create();
    public void Dispose() => TempDir.Delete(_dir);

    [Fact]
    public void Write_then_Read_roundtrips_field_values()
    {
        var fields = FieldSchemas.Pricing;
        var path = Path.Combine(_dir, "清单表格.xlsx");
        var rows = new[]
        {
            new MaterialRow { RowKey = "M-1", Values = new() {
                ["materialCode"] = "M-1", ["model"] = "GB-1", ["name"] = "支架 / Q235",
                ["demandQty"] = "10", ["hasChange"] = "无变更", ["targetPrice"] = "88.5" } },
            new MaterialRow { RowKey = "M-2", Values = new() {
                ["materialCode"] = "M-2", ["model"] = "GB-2", ["name"] = "法兰 / 304",
                ["demandQty"] = "20", ["hasChange"] = "有变更", ["targetPrice"] = null } },
        };

        ExcelGridIO.Write(path, fields, rows);
        var read = ExcelGridIO.Read(path, fields);

        Assert.Equal(2, read.Count);

        var r1 = read.Single(x => x.RowKey == "M-1").Values;
        Assert.Equal("GB-1", r1["model"]);
        Assert.Equal("支架 / Q235", r1["name"]);
        Assert.Equal("88.5", r1["targetPrice"]);

        var r2 = read.Single(x => x.RowKey == "M-2").Values;
        Assert.Null(r2["targetPrice"]); // 空待填列读回为 null
    }

    [Fact]
    public void Read_missing_file_returns_empty()
    {
        var read = ExcelGridIO.Read(Path.Combine(_dir, "nope.xlsx"), FieldSchemas.Pricing);
        Assert.Empty(read);
    }
}
