using NPOI.SS.UserModel;
using Draftline.Core.Contracts.Http;
using Draftline.Core.Enums;
using Draftline.Core.Models;
using Draftline.Gateway.Stores;

namespace Draftline.Tests.Storage;

/// <summary>
/// 服务端 Excel 写入并发锁（changes/009 #1）。无锁时并发 read-modify-write 会 IOException
/// 或写坏 xlsx；加锁后整批并发更新应全部成功且每行值正确。
/// </summary>
public class ServerBatchStoreConcurrencyTests
{
    [Fact]
    public async Task UpdateExcelRow_Concurrent_AllSucceedAndValuesCorrect()
    {
        var root = TempDir.Create();
        try
        {
            var store = new FileServerBatchStore(new ServerStorageOptions { ServerRoot = root }, new ServerFieldProvider());
            var ws = new DateTime(2026, 6, 18, 9, 31, 0, DateTimeKind.Utc);
            var we = new DateTime(2026, 6, 18, 15, 30, 0, DateTimeKind.Utc);
            var batchId = LocalPaths.BatchFolderName(ws, we);
            const string group = "组1";
            const int n = 16;

            var resp = new FetchResponse
            {
                Success = true,
                Flow = FlowType.Pricing,
                EmployeeId = "10086",
                WindowStart = ws,
                WindowEnd = we,
                GroupName = group,
                Rows = Enumerable.Range(0, n).Select(i => new FetchRowDto
                {
                    RowKey = $"M-{1000 + i}",
                    Values = new Dictionary<string, string?> { ["materialCode"] = $"M-{1000 + i}" }
                }).ToList()
            };
            await store.SaveBatchAsync(resp, group, Array.Empty<(string, byte[])>());

            // 并发对每一行写 targetPrice；无锁会抛/损坏，有锁应全绿。
            var tasks = Enumerable.Range(0, n).Select(i => store.UpdateExcelRowAsync(
                FlowType.Pricing, group, batchId, $"M-{1000 + i}",
                new Dictionary<string, string?> { ["targetPrice"] = (i * 10).ToString() })).ToArray();

            await Task.WhenAll(tasks);

            // 重新读回，校验每行 targetPrice 都正确落盘（文件未损坏 + 写入未相互覆盖）。
            var values = ReadTargetPrices(store, batchId, group);
            Assert.Equal(n, values.Count);
            for (int i = 0; i < n; i++)
                Assert.Equal((i * 10).ToString(), values[$"M-{1000 + i}"]);
        }
        finally
        {
            TempDir.Delete(root);
        }
    }

    private static Dictionary<string, string> ReadTargetPrices(FileServerBatchStore store, string batchId, string group)
    {
        using var stream = store.OpenFile(FlowType.Pricing, group, batchId, LocalFolders.GridWorkbookName(FlowType.Pricing, batchId))
                           ?? throw new InvalidOperationException("批次 Excel 未找到。");
        var workbook = WorkbookFactory.Create(stream);
        var sheet = workbook.GetSheetAt(0);
        var header = sheet.GetRow(0);

        int codeCol = -1, priceCol = -1;
        for (int c = 0; c < header.LastCellNum; c++)
        {
            var text = header.GetCell(c)?.StringCellValue.Trim();
            if (text == "物料编码") codeCol = c;
            else if (text == "目标价") priceCol = c;
        }

        var result = new Dictionary<string, string>();
        for (int r = 1; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            var code = row.GetCell(codeCol)?.ToString();
            if (string.IsNullOrEmpty(code)) continue;
            result[code] = row.GetCell(priceCol)?.ToString() ?? "";
        }
        return result;
    }
}
