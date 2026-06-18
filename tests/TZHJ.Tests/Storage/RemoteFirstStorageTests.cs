using TZHJ.Core.Contracts.Http;
using TZHJ.Core.Enums;
using TZHJ.Core.Models;
using TZHJ.Gateway.Stores;
using TZHJ.Infrastructure.Fields;
using TZHJ.Infrastructure.Storage;
using Xunit;

namespace TZHJ.Tests.Storage;

public class RemoteFirstStorageTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FileServerBatchStore _store;
    private readonly ServerFieldProvider _fields;

    public RemoteFirstStorageTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "TZHJ_ServerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        
        _fields = new ServerFieldProvider();
        _store = new FileServerBatchStore(new ServerStorageOptions { ServerRoot = _tempRoot }, _fields);
    }

    [Fact]
    public async Task SaveBatch_ShouldCreatePhysicalFiles()
    {
        // Arrange
        var empId = "test_user";
        var group = "核价一组";
        var flow = FlowType.Pricing;
        var start = new DateTime(2026, 6, 12, 9, 0, 0);
        var end = new DateTime(2026, 6, 12, 10, 0, 0);
        var batchId = "20260612_0900-1000";

        var resp = new FetchResponse
        {
            EmployeeId = empId,
            Flow = flow,
            WindowStart = start,
            WindowEnd = end,
            GroupName = group,
            Rows = new List<FetchRowDto>
            {
                new() { RowKey = "M1001", Values = new Dictionary<string, string?> { ["materialCode"] = "M1001", ["targetPrice"] = "10.5" } }
            }
        };
        var drawings = new List<(string, byte[])> { ("M1001__A.pdf", new byte[] { 1, 2, 3 }) };

        // Act
        await _store.SaveBatchAsync(resp, group, drawings);

        // Assert
        var batchDir = LocalPaths.ServerBatchDir(_tempRoot, flow, group, batchId);
        Assert.True(Directory.Exists(batchDir));
        Assert.True(File.Exists(Path.Combine(batchDir, LocalFolders.GridWorkbookName(batchId))));
        Assert.True(File.Exists(Path.Combine(batchDir, "M1001__A.pdf")));
    }

    [Fact]
    public async Task UpdateExcelRow_ShouldModifyPhysicalCell()
    {
        // Arrange
        var empId = "test_user";
        var group = "核价二组";
        var flow = FlowType.Pricing;
        var batchId = "20260612_1100-1200";
        var rowKey = "M1001";
        
        // 1. 先准备一个初始批次
        var initialResp = new FetchResponse
        {
            EmployeeId = empId,
            Flow = flow,
            WindowStart = new DateTime(2026, 6, 12, 11, 0, 0),
            WindowEnd = new DateTime(2026, 6, 12, 12, 0, 0),
            GroupName = group,
            Rows = new List<FetchRowDto>
            {
                new() { RowKey = rowKey, Values = new Dictionary<string, string?> { ["materialCode"] = "M1001", ["targetPrice"] = "0" } }
            }
        };
        await _store.SaveBatchAsync(initialResp, group, Enumerable.Empty<(string, byte[])>());

        // 2. 准备更新
        var newValues = new Dictionary<string, string?> { ["targetPrice"] = "99.99" };

        // Act
        await _store.UpdateExcelRowAsync(flow, group, batchId, rowKey, newValues);

        // Assert
        var excelPath = Path.Combine(LocalPaths.ServerBatchDir(_tempRoot, flow, group, batchId), LocalFolders.GridWorkbookName(batchId));
        var fields = _fields.FieldsFor(flow);
        var readBack = ExcelGridIO.Read(excelPath, fields);
        
        var updatedRow = readBack.First(r => r.RowKey == rowKey);
        Assert.Equal("99.99", updatedRow.Values["targetPrice"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, true);
    }
}
