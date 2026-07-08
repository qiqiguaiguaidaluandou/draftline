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
    /// 3. 镜像删除：本地存在、但 Catalog 已无（含被服务器过期清理掉）的批次 → 删本地目录，保持"本地=云端"。
    /// 4. 异常池联动：全量覆盖本地异常池。
    /// </summary>
    /// <param name="allowedFlows">
    /// 镜像删除的作用范围：仅在操作员有权限的流程内清理本地孤儿批次。
    /// 传空集合（如无任何授权）时不删除任何本地数据，避免误删。
    /// </param>
    public async Task<BatchSyncResult> MirrorSyncAsync(string employeeId, IReadOnlyCollection<FlowType> allowedFlows, CancellationToken ct = default)
    {
        var result = new BatchSyncResult();
        var localRoot = _store.Root;

        // 1. 同步批次
        var catalog = await _data.GetCatalogAsync(ct);

        // 云端现存批次的键集（流程+组+批次），用于第 3 步镜像删除的判定。
        var catalogKeys = catalog
            .Select(i => BatchKey(i.Flow, i.GroupName, i.BatchId))
            .ToHashSet();

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

        // 2. 镜像删除（prune）：本地有、但云端 catalog 已无的批次 → 删本地目录。
        //    覆盖"服务器过期清理后本地也应消失"的诉求（服务器 Done 15 天 / Todo 30 天到期删除后，
        //    对应批次不再出现在 catalog，这里据此删本地）。
        //    · 只按键 (流程+组+批次) 匹配、忽略位置：凡云端仍存在的批次绝不会被删（安全）。
        //    · 只清 allowedFlows 内的流程：无授权则 allowedFlows 为空 → 不动任何本地数据。
        //    · catalog 拉取失败会在上面 GetCatalogAsync 处抛出、根本走不到这里，故不会因网络抖动误删。
        foreach (var flow in allowedFlows.Distinct())
        {
            foreach (var location in new[] { BatchLocation.Todo, BatchLocation.Done })
            {
                var locationRoot = LocalPaths.LocalLocationRoot(localRoot, flow, location);
                if (!Directory.Exists(locationRoot)) continue;

                foreach (var (groupName, batchDir) in EnumerateLocalBatchDirs(locationRoot))
                {
                    ct.ThrowIfCancellationRequested();
                    var batchId = Path.GetFileName(batchDir);
                    if (catalogKeys.Contains(BatchKey(flow, groupName, batchId))) continue; // 云端还在 → 保留

                    try
                    {
                        Directory.Delete(batchDir, recursive: true);
                        result.Pruned++;
                    }
                    catch
                    {
                        result.Failed++;
                    }
                }
            }
        }

        // 3. 同步异常池 (按流程归类镜像)
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

    /// <summary>镜像删除的批次唯一键：流程 + 组 + 批次号（与位置无关）。</summary>
    private static string BatchKey(FlowType flow, string groupName, string batchId) => $"{flow}|{groupName}|{batchId}";

    /// <summary>
    /// 枚举某"位置根"（如 核价/待处理）下的本地批次目录，返回 (组名, 批次目录全路径)。
    /// 目录层级固定为 位置/组/批次（挑图组名恒为 "Center"，核价为组名），故取两层。
    /// </summary>
    private static IEnumerable<(string GroupName, string BatchDir)> EnumerateLocalBatchDirs(string locationRoot)
    {
        foreach (var groupDir in Directory.GetDirectories(locationRoot))
        {
            var groupName = Path.GetFileName(groupDir);
            foreach (var batchDir in Directory.GetDirectories(groupDir))
                yield return (groupName, batchDir);
        }
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

    /// <summary>本轮镜像删除掉的本地孤儿批次数（云端已无 → 删本地）。</summary>
    public int Pruned { get; set; }

    public int Fetched => NewBatches.Count;
}
