using TZHJ.Core.Contracts;
using TZHJ.Core.Contracts.Http;
using TZHJ.Core.Enums;
using TZHJ.Core.Models;
using TZHJ.Gateway.AntiCorruption;
using TZHJ.Gateway.Stores;
using Microsoft.EntityFrameworkCore;

namespace TZHJ.Gateway;

/// <summary>
/// 后端定时采集服务（模拟服务器主动抓取 EBS/PLM 数据）。
/// 运行于服务器后台，不依赖客户端。抓取后存入服务器路径并注册到数据库。
/// </summary>
public sealed class DataIngestionService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<DataIngestionService> _logger;
    private readonly IConfiguration _config;

    public DataIngestionService(IServiceProvider sp, ILogger<DataIngestionService> logger, IConfiguration config)
    {
        _sp = sp;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Data Ingestion Service started. Seeding historical data (last 3 hours)...");

        // 1. 启动时：补齐过去 3 小时的历史数据 + 清理过期数据
        await SeedHistoryAsync(stoppingToken);
        await CleanupOldBatchesAsync(stoppingToken);

        // 2. 运行中：每 5 分钟检查一次最新窗
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SeedLatestAsync(stoppingToken);
                
                // 周期性清理（每小时执行一次较合适，此处简单跟随时钟）
                if (DateTime.Now.Minute % 60 == 0)
                {
                    await CleanupOldBatchesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during periodical data ingestion/cleanup.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task CleanupOldBatchesAsync(CancellationToken ct)
    {
        var todoDays = _config.GetValue<int>("Config:RetentionDaysForTodo", 30);
        var doneDays = _config.GetValue<int>("Config:RetentionDaysForDone", 15);

        var todoThreshold = DateTime.UtcNow.AddDays(-todoDays);
        var doneThreshold = DateTime.UtcNow.AddDays(-doneDays);

        _logger.LogInformation("Batch cleanup started. Todo Threshold: {Todo:O} ({TodoDays}d), Done Threshold: {Done:O} ({DoneDays}d)", 
            todoThreshold, todoDays, doneThreshold, doneDays);

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TzhjDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IServerBatchStore>();

        // 找出过期的批次
        var expired = await db.BatchRegistries
            .Where(b => (b.Status == BatchLocation.Todo && b.LastModified < todoThreshold)
                     || (b.Status == BatchLocation.Done && b.LastModified < doneThreshold))
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        foreach (var b in expired)
        {
            try
            {
                _logger.LogInformation("Deleting expired batch ({Status}): {Flow} - {Group} - {BatchId}", 
                    b.Status, b.Flow, b.GroupName, b.BatchId);

                // 1. 物理删除
                await store.DeleteBatchAsync(b.Flow, b.GroupName, b.BatchId, ct);

                // 2. 数据库删除
                db.BatchRegistries.Remove(b);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete expired batch {BatchId}", b.BatchId);
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Cleanup completed. Removed {Count} batches.", expired.Count);
    }

    private async Task SeedHistoryAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        var pricingGroups = new[] { "组1", "组2" };
        var flows = new[] { FlowType.Pricing, FlowType.DrawingSelection };

        // 循环过去 3 个小时窗
        for (int i = 0; i < 3; i++)
        {
            if (ct.IsCancellationRequested) break;

            var windowEnd = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(-i);
            var windowStart = windowEnd.AddHours(-1);

            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TzhjDbContext>();
            var source = scope.ServiceProvider.GetRequiredService<IEbsPlmSource>();
            var store = scope.ServiceProvider.GetRequiredService<IServerBatchStore>();

            foreach (var flow in flows)
            {
                if (flow == FlowType.DrawingSelection)
                {
                    // 挑图流程：不分组，直接生成
                    await IngestAsync(db, source, store, flow, "Center", windowStart, windowEnd, ct);
                }
                else
                {
                    // 核价流程：按组生成
                    foreach (var group in pricingGroups)
                    {
                        await IngestAsync(db, source, store, flow, group, windowStart, windowEnd, ct);
                    }
                }
            }
        }
        _logger.LogInformation("Historical data seeding completed.");
    }

    private async Task SeedLatestAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        var windowEnd = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        var windowStart = windowEnd.AddHours(-1);

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TzhjDbContext>();
        var source = scope.ServiceProvider.GetRequiredService<IEbsPlmSource>();
        var store = scope.ServiceProvider.GetRequiredService<IServerBatchStore>();

        foreach (var flow in new[] { FlowType.Pricing, FlowType.DrawingSelection })
        {
            if (flow == FlowType.DrawingSelection)
            {
                await IngestAsync(db, source, store, flow, "Center", windowStart, windowEnd, ct);
            }
            else
            {
                foreach (var group in new[] { "组1", "组2" })
                {
                    await IngestAsync(db, source, store, flow, group, windowStart, windowEnd, ct);
                }
            }
        }
    }

    private async Task IngestAsync(TzhjDbContext db, IEbsPlmSource source, IServerBatchStore store, FlowType flow, string groupName, DateTime ws, DateTime we, CancellationToken ct)
    {
        var batchId = LocalPaths.BatchFolderName(ws, we);
        
        // 1. 检查物理磁盘是否存在
        var serverBatchDir = LocalPaths.ServerBatchDir(store.Root, flow, groupName, batchId);
        bool diskExists = Directory.Exists(serverBatchDir);

        // 2. 检查数据库记录
        var registry = await db.BatchRegistries.FirstOrDefaultAsync(b => b.BatchId == batchId && b.GroupName == groupName && b.Flow == flow, ct);

        // 只有当【物理存在】且【数据库记录也存在】时，才真正跳过
        if (diskExists && registry != null)
        {
            return;
        }

        _logger.LogInformation("Ingesting/Healing batch: {Flow} - {GroupName} - {BatchId} (Disk:{Disk}, DB:{Db})", 
            flow, groupName, batchId, diskExists, registry != null);

        // 3. 从防腐层抓取 (FakeDataSource)
        var rows = await source.FetchRowsAsync(flow, "SYSTEM", ws, we, ct);
        if (rows.Count == 0) return;

        var resp = new FetchResponse
        {
            Success = true,
            Flow = flow,
            EmployeeId = "SYSTEM",
            WindowStart = ws,
            WindowEnd = we,
            GroupName = groupName,
            Rows = rows.Select(r => new FetchRowDto
            {
                RowKey = r.RowKey,
                Values = r.Values,
                Drawings = r.Drawings.Select(d => new DrawingMeta
                {
                    DrawingId = d.DrawingId,
                    FileName = d.FileName,
                    MaterialCode = d.MaterialCode,
                    Size = d.Content.LongLength,
                }).ToList(),
            }).ToList(),
        };

        // 4. 强制持久化到服务器磁盘 (补齐物理文件)
        var drawings = rows.SelectMany(r => r.Drawings).Select(d => (d.FileName, d.Content));
        await store.SaveBatchAsync(resp, groupName, drawings, ct);

        // 5. 注册或刷新数据库状态
        if (registry == null)
        {
            registry = new BatchRegistry
            {
                BatchId = batchId,
                GroupName = groupName,
                Flow = flow,
                Status = BatchLocation.Todo,
                TotalRows = rows.Count, // 记录总行数
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow
            };
            db.BatchRegistries.Add(registry);
        }
        else
        {
            registry.TotalRows = rows.Count;
            registry.LastModified = DateTime.UtcNow; 
        }

        // 6. 记录系统日志
        db.ActivityLogs.Add(new ActivityLog
        {
            Action = "Ingest",
            EmployeeId = "SYSTEM",
            Flow = flow,
            GroupName = groupName,
            BatchId = batchId,
            ImpactCount = rows.Count,
            Status = "Success",
            Timestamp = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }
}
