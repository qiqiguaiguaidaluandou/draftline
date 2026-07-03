using Draftline.Core.Contracts;
using Draftline.Core.Enums;
using Draftline.Core.Models;
using Draftline.Infrastructure.Storage;

namespace Draftline.Infrastructure.Sync;

/// <summary>
/// 补拉编排（手动补拉 / 登录补拉 / 会话内定时触发 共用同一套）。对某流程：
/// 取"已关闭（已到触发时刻）"的时间窗 → 本地已有则跳 → 审计命中则跳（已回传过，不重拉，D6） → 否则取数落本地。
/// 幂等：重复调用只会补真正缺的窗，故定时轮询安全。无 WPF 依赖，可跨平台单测（时间经 now 参数注入）。
/// </summary>
public sealed class BatchSyncService
{
    private readonly ILocalBatchStore _store;
    private readonly IDataGateway _data;
    private readonly IAuditGateway _audit;

    public BatchSyncService(ILocalBatchStore store, IDataGateway data, IAuditGateway audit)
    {
        _store = store;
        _data = data;
        _audit = audit;
    }

    /// <summary>
    /// 全量镜像同步：
    /// 1. 拉取服务器 Catalog，补齐本地缺失文件或更新已变动文件。
    /// 2. 状态联动：如果服务器状态与本地位置不一致（如云端变 Done），则移动本地目录。
    /// 3. 异常池联动：全量覆盖本地异常池。
    /// </summary>
    public async Task<BatchSyncResult> MirrorSyncAsync(string employeeId, CancellationToken ct = default)
    {
        var result = new BatchSyncResult();
        var localRoot = _store.Root;

        // 1. 同步批次
        var catalog = await _data.GetCatalogAsync(ct);
        foreach (var item in catalog)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // 确定同步范围：Todo 全量，Done 15天
                LocalPaths.TryParseFolderName(item.BatchId, out var windowStart, out var windowEnd);
                bool isRecentDone = item.Status == BatchLocation.Done && windowEnd > DateTime.Now.AddDays(-15);
                bool shouldSync = item.Status == BatchLocation.Todo || isRecentDone;

                if (!shouldSync) continue;

                // 【状态感知】检查本地是否存在于“错误”的位置
                var otherLocation = item.Status == BatchLocation.Todo ? BatchLocation.Done : BatchLocation.Todo;
                var wrongDir = LocalPaths.LocalBatchDir(localRoot, item.Flow, otherLocation, item.GroupName, item.BatchId);
                var targetDir = LocalPaths.LocalBatchDir(localRoot, item.Flow, item.Status, item.GroupName, item.BatchId);

                if (Directory.Exists(wrongDir))
                {
                    // 本地位置不对（例如云端已完成，但本地还在待处理），执行移动
                    Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);
                    if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
                    Directory.Move(wrongDir, targetDir);
                }

                // 强制确保物理层级存在（ data/流程/状态/组/批次 ）
                Directory.CreateDirectory(targetDir);
                
                // --- 优化：预先写入包含 TotalRows 的占位 Manifest ---
                var manifestPath = Path.Combine(targetDir, LocalFolders.Manifest);
                var manifest = await BatchManifest.LoadAsync(manifestPath, ct) ?? new BatchManifest
                {
                    Flow = item.Flow,
                    EmployeeId = "(group)",
                    WindowStart = windowStart,
                    WindowEnd = windowEnd,
                    FetchedAt = DateTime.Now,
                };
                manifest.TotalRows = item.TotalRows; // 同步云端统计
                await BatchManifest.SaveAsync(manifestPath, manifest, ct);

                await _store.EnsureBatchFolderAsync(item.Flow, item.GroupName, item.BatchId, item.Status, ct);

                // 同步文件
                foreach (var remoteFile in item.Files)
                {
                    var fullLocalPath = Path.Combine(targetDir, remoteFile.FileName);
                    
                    bool needsDownload = !File.Exists(fullLocalPath) 
                                         || new FileInfo(fullLocalPath).Length != remoteFile.Size
                                         || File.GetLastWriteTime(fullLocalPath) < remoteFile.LastModified.AddSeconds(-1);

                    if (needsDownload)
                    {
                        var bytes = await _data.DownloadFileAsync(item.Flow, item.GroupName, item.BatchId, remoteFile.FileName, ct);
                        await _store.WriteSyncFileAsync(item.Flow, item.GroupName, item.BatchId, item.Status, remoteFile.FileName, bytes, ct);
                    }
                }
                
                result.NewBatches.Add(new Batch 
                { 
                    FolderName = item.BatchId, 
                    Flow = item.Flow,
                    EmployeeId = employeeId,
                    GroupName = item.GroupName,
                    WindowStart = windowStart,
                    WindowEnd = windowEnd,
                    Location = item.Status,
                    FolderPath = targetDir
                });
            }
            catch
            {
                result.Failed++;
            }
        }

        // 2. 同步异常池 (按流程归类镜像)
        var allExceptions = await _data.GetExceptionsAsync(ct);
        
        // 分组处理：确保核价的异常去核价文件夹，挑图的异常去挑图文件夹
        var groups = allExceptions.GroupBy(e => e.Flow);
        
        // 覆盖已授权流程的本地记录
        var processedFlows = new HashSet<FlowType>();
        foreach (var group in groups)
        {
            await _store.OverwriteExceptionsAsync(group.Key, "AllGroups", group.ToList(), ct);
            processedFlows.Add(group.Key);
        }

        // 如果某个流程云端已无异常，也需要清空本地
        var allFlows = new[] { FlowType.Pricing, FlowType.DrawingSelection };
        foreach (var flow in allFlows.Where(f => !processedFlows.Contains(f)))
        {
            await _store.OverwriteExceptionsAsync(flow, "AllGroups", new List<ExceptionItem>(), ct);
        }

        return result;
    }

    /// <summary>
    /// 今天 + 昨天锚定下、**已到触发时刻（窗口关闭后 delayMinutes 分钟）** 的窗，按止时间倒序。
    /// 尚未关闭或未达缓冲时间的窗一律排除。
    /// </summary>
    public static IEnumerable<(DateTime Start, DateTime End)> ClosedWindows(
        IReadOnlyList<CollectionWindow> windows, DateTime now, int delayMinutes = 1)
    {
        var today = DateOnly.FromDateTime(now);
        var list = new List<(DateTime Start, DateTime End)>();
        foreach (var anchor in new[] { today, today.AddDays(-1) })
            foreach (var w in windows.Where(w => w.Enabled))
            {
                var triggerTime = anchor.ToDateTime(w.EndTime).AddMinutes(delayMinutes);
                if (triggerTime > now) continue; // 未到触发时刻
                list.Add(w.Resolve(anchor));
            }
        return list.OrderByDescending(x => x.End);
    }
}

/// <summary>补拉一轮的结果。</summary>
public sealed class BatchSyncResult
{
    public List<Batch> NewBatches { get; } = new();
    public int SkippedLocal { get; set; }
    public int SkippedAudit { get; set; }
    public int Failed { get; set; }

    public int Fetched => NewBatches.Count;
}
