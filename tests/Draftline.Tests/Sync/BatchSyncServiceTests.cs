using Draftline.Core.Contracts;
using Draftline.Core.Contracts.Http;
using Draftline.Core.Enums;
using Draftline.Core.Models;
using Draftline.Infrastructure.Sync;

namespace Draftline.Tests.Sync;

public class BatchSyncServiceTests
{
    private const string Emp = "10086";

    private static (BatchSyncService svc, FakeLocalBatchStore store, FakeDataGateway data, FakeAuditGateway audit) Build()
    {
        var store = new FakeLocalBatchStore();
        var data = new FakeDataGateway();
        var audit = new FakeAuditGateway();
        return (new BatchSyncService(store, data, audit), store, data, audit);
    }

    [Fact]
    public async Task MirrorSync_downloads_missing_files_from_catalog()
    {
        var (svc, store, data, _) = Build();
        var windowStart = DateTime.Now.AddHours(-1);
        var windowEnd = DateTime.Now;
        var batchId = LocalPaths.BatchFolderName(windowStart, windowEnd);

        // 模拟服务器有一个待处理批次，包含一个文件
        data.Catalog.Add(new BatchCatalogItem
        {
            BatchId = batchId,
            Flow = FlowType.Pricing,
            GroupName = "Group1",
            Status = BatchLocation.Todo,
            LastModified = DateTime.Now,
            Files = new List<SyncFileMeta>
            {
                new SyncFileMeta { FileName = "清单表格.xlsx", Size = 100, LastModified = DateTime.Now }
            }
        });

        var result = await svc.MirrorSyncAsync(Emp);

        Assert.Equal(1, result.Fetched);
        Assert.Equal(batchId, result.NewBatches[0].FolderName);
        // 这里我们可以进一步验证是否调用了 DownloadFileAsync，但 FakeDataGateway 还没记这个日志。
    }

    [Fact]
    public async Task MirrorSync_skips_old_done_batches()
    {
        var (svc, store, data, _) = Build();
        var oldDate = DateTime.Now.AddDays(-20);
        var batchId = LocalPaths.BatchFolderName(oldDate, oldDate.AddHours(1));

        data.Catalog.Add(new BatchCatalogItem
        {
            BatchId = batchId,
            Flow = FlowType.Pricing,
            GroupName = "Group1",
            Status = BatchLocation.Done,
            LastModified = oldDate
        });

        var result = await svc.MirrorSyncAsync(Emp);

        Assert.Equal(0, result.Fetched); // 超过 15 天的 Done 不同步
    }
}
