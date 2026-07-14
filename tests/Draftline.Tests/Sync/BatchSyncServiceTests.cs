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

        var result = await svc.MirrorSyncAsync(Emp, Array.Empty<FlowType>());

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
            TotalRows = 5, // 有数据的已处理批次
            LastModified = oldDate
        });

        var result = await svc.MirrorSyncAsync(Emp, Array.Empty<FlowType>());

        Assert.Equal(0, result.Fetched); // 超过 15 天、有数据的 Done 不同步
    }

    [Fact]
    public async Task MirrorSync_downloads_empty_done_batch_even_when_old()
    {
        // 空批次（0 行）出生即 Done、客户端从没见它 Todo。即便数据窗口超过 15 天（追赶旧窗/首次部署回填），
        // 也应无条件镜像到本地，避免"服务器上有、本地没有"。
        var (svc, store, data, _) = Build();
        var root = TempDir.Create();
        try
        {
            store.Root = root;
            var oldDate = DateTime.Now.AddDays(-20);
            var batchId = LocalPaths.BatchFolderName(oldDate, oldDate.AddHours(1));

            data.Catalog.Add(new BatchCatalogItem
            {
                BatchId = batchId,
                Flow = FlowType.Pricing,
                GroupName = "Group1",
                Status = BatchLocation.Done,
                TotalRows = 0, // 空批次
                LastModified = DateTime.Now // 刚落库
            });

            var result = await svc.MirrorSyncAsync(Emp, Array.Empty<FlowType>());

            Assert.Equal(1, result.Fetched); // 空批次即便窗口旧也同步
            Assert.Equal(batchId, result.NewBatches[0].FolderName);
            Assert.Equal(BatchLocation.Done, result.NewBatches[0].Location);
        }
        finally { TempDir.Delete(root); }
    }

    [Fact]
    public async Task MirrorSync_prunes_local_batch_absent_from_catalog()
    {
        var (svc, store, data, _) = Build();
        var root = TempDir.Create();
        try
        {
            store.Root = root;

            // 本地预置一个已同步下来的批次目录，但服务器 catalog 已无（模拟被过期清理）。
            var staleBatchId = LocalPaths.BatchFolderName(DateTime.Now.AddHours(-2), DateTime.Now.AddHours(-1));
            var staleDir = LocalPaths.LocalBatchDir(root, FlowType.Pricing, BatchLocation.Todo, "Group1", staleBatchId);
            Directory.CreateDirectory(staleDir);

            // 另置一个云端仍存在的批次目录，验证不会被误删。
            var keepBatchId = LocalPaths.BatchFolderName(DateTime.Now.AddMinutes(-30), DateTime.Now);
            var keepDir = LocalPaths.LocalBatchDir(root, FlowType.Pricing, BatchLocation.Todo, "Group1", keepBatchId);
            Directory.CreateDirectory(keepDir);
            data.Catalog.Add(new BatchCatalogItem
            {
                BatchId = keepBatchId,
                Flow = FlowType.Pricing,
                GroupName = "Group1",
                Status = BatchLocation.Todo,
                LastModified = DateTime.Now
            });

            var result = await svc.MirrorSyncAsync(Emp, new[] { FlowType.Pricing });

            Assert.False(Directory.Exists(staleDir)); // 云端已无 → 本地删除
            Assert.True(Directory.Exists(keepDir));    // 云端仍在 → 本地保留
            Assert.Equal(1, result.Pruned);
        }
        finally { TempDir.Delete(root); }
    }
}
