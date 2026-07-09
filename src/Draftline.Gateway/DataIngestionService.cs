using System.Globalization;
using Draftline.Core.Contracts;
using Draftline.Core.Contracts.Http;
using Draftline.Core.Enums;
using Draftline.Core.Models;
using Draftline.Core.Schemas;
using Draftline.Gateway.AntiCorruption;
using Draftline.Gateway.Stores;
using Microsoft.EntityFrameworkCore;

namespace Draftline.Gateway;

/// <summary>
/// 后端定时采集服务（模拟服务器主动抓取 EBS/PLM 数据）。
/// 运行于服务器后台，不依赖客户端。抓取后存入服务器路径并注册到数据库。
/// </summary>
public sealed class DataIngestionService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<DataIngestionService> _logger;
    private readonly IConfiguration _config;
    private readonly EbsOptions _ebs;

    // 各窗口连续失败轮数（键 = "flow:batchId"）：识别"疑似永久坏窗"并升级告警。
    // BackgroundService 单例、执行循环单线程，无并发访问；键在窗口采成功时移除，故不会泄漏。
    private readonly Dictionary<string, int> _failStreak = new();
    private const int StuckWindowRounds = 6; // 连续 6 轮（约 30 分钟）仍失败 → 视为疑似永久坏窗。

    public DataIngestionService(IServiceProvider sp, ILogger<DataIngestionService> logger, IConfiguration config, EbsOptions ebs)
    {
        _sp = sp;
        _logger = logger;
        _config = config;
        _ebs = ebs;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Data Ingestion Service started. 按排程采集（核价 10:00/16:00；挑图 10:00/15:00/18:30）。");

        // 启动先清一次过期批次；采集本身由下面的循环按排程触发（含重启后补采）。
        await CleanupOldBatchesAsync(stoppingToken);
        var lastCleanup = DateTime.UtcNow;
        var cleanupInterval = TimeSpan.FromHours(6); // 每 6 小时清理一次（一天约 4 次）；过期判定按天，无需更频繁。

        // 每 5 分钟检查一次排程：到点而未采的批次就采（已采的会跳过，不重复调接口）。
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckSchedulesAsync(stoppingToken);

                // 周期性清理：以"距上次清理已满 cleanupInterval"为准触发。
                // 旧写法 `DateTime.Now.Minute % 60 == 0`（即 Minute==0）依赖 5 分钟 tick 恰好落在整点分钟，
                // 而 Task.Delay 会漂移、不对齐时钟，某些启动时刻会长期错过 minute==0 → 清理一直不触发。
                if (DateTime.UtcNow - lastCleanup >= cleanupInterval)
                {
                    await CleanupOldBatchesAsync(stoppingToken);
                    lastCleanup = DateTime.UtcNow;
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
        var db = scope.ServiceProvider.GetRequiredService<DraftlineDbContext>();
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

    // 采集排程的唯一权威定义在 Draftline.Core.Schemas.IngestionSchedules（客户端补拉/后台展示共用同一套）。

    /// <summary>
    /// 采集排程使用的"当前时间"。配置 `Ingestion:NowOverride`（如 "2026-05-28 17:00:00"）时返回该固定值，
    /// 仅用于**开发期临时**把排程窗口挪到有数据的历史日期来验证全链路。
    /// 只影响排程窗口计算，不动鉴权 JWT、批次清理、各类时间戳（它们仍用真实时间）。
    /// 改回真实时间：把配置项清空即可，无需改代码。
    /// </summary>
    private DateTime SchedulingNow()
    {
        var ovr = _config["Ingestion:NowOverride"];
        if (!string.IsNullOrWhiteSpace(ovr) &&
            DateTime.TryParse(ovr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            _logger.LogWarning("⚠ 采集排程正在使用覆盖时间 {Override}（Ingestion:NowOverride），仅供测试，验证完请清空该配置！", dt);
            return dt;
        }
        return DateTime.Now;
    }

    /// <summary>
    /// 检查所有排程并按「数据时间轴高水位 T_covered」模型取数（见 docs/changes/023）。
    /// 每流程：先从已登记批次派生 T_covered → 由 <see cref="IngestionPlanner.Plan"/> 算出本轮该采的窗口序列
    /// （回看 MaxCatchupDays 天、已过"到点 + 未覆盖"两道闸门、按窗口止升序）→ 逐窗取数。
    /// 取数从 T_covered 之后接续 → 改窗不倒补、切换不丢数；日常/宕机补采均产出标准窗口批次。
    /// 某窗失败即就地停止本流程本轮（不越过留洞），下一轮以真实 T_covered 重算重试。
    /// </summary>
    private async Task CheckSchedulesAsync(CancellationToken ct)
    {
        var now = SchedulingNow();
        // 回看日历天数（含今天）。默认 2 = 昨天+今天，对齐现状；宕机久了可临时调大再重启补更久历史。
        // 该值同时是"每轮回看范围"（锚在 now）与"追赶上限"——二者是同一枚举动作的两面（见 docs/changes/023）。
        // clamp 下限为 1：若误配为 0/负数，Plan 会枚举不到任何窗口 → 静默停采。
        var horizonDays = Math.Max(1, _config.GetValue<int>("Ingestion:MaxCatchupDays", 2));

        foreach (var flow in new[] { FlowType.Pricing, FlowType.DrawingSelection })
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                await IngestFlowAsync(flow, now, horizonDays, ct);
            }
            catch (Exception ex)
            {
                // 每流程隔离：算高水位/规划阶段的异常只跳过本流程本轮，不连累另一流程（下轮重试）。
                _logger.LogError(ex, "流程 {Flow} 本轮采集调度失败（跳过本流程，下轮重试）。", flow);
            }
        }
    }

    /// <summary>处理单个流程一轮：算 T_covered → 规划 → 逐窗取数（某窗失败即就地停止本流程本轮，不越过留洞）。</summary>
    private async Task IngestFlowAsync(FlowType flow, DateTime now, int horizonDays, CancellationToken ct)
    {
        // 期望组别：与 IngestWindowAsync 内一致（单一来源）。核价固定配置组，挑图不分组只有 Center。
        var expectedGroups = flow == FlowType.Pricing ? _ebs.PricingGroups : new[] { "Center" };

        DateTime? tCovered;
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DraftlineDbContext>();
            tCovered = await ComputeTCoveredAsync(db, flow, expectedGroups, ct);
        }

        var plan = IngestionPlanner.Plan(now, tCovered, horizonDays, IngestionSchedules.For(flow));

        // 追赶超上限留痕：已有高水位、但本轮起点晚于"接续点"，说明超过 horizon 的更久远数据被跳过（不补）。
        if (tCovered is DateTime behind && plan.Count > 0 && plan[0].EffStart > behind.AddMinutes(1))
        {
            _logger.LogWarning(
                "追赶超上限({Cap}天)：{Flow} 已覆盖到 {Covered:yyyy-MM-dd HH:mm}，本轮从 {From:yyyy-MM-dd HH:mm} 起补，" +
                "跳过 {GapStart:yyyy-MM-dd HH:mm}~{GapEnd:yyyy-MM-dd HH:mm} 的更久远数据（不补；如需补请临时调大 Ingestion:MaxCatchupDays 再重启，也可能是排程平铺存在缺口）。",
                horizonDays, flow, behind, plan[0].EffStart, behind.AddMinutes(1), plan[0].EffStart.AddMinutes(-1));
        }

        foreach (var w in plan)
        {
            if (ct.IsCancellationRequested) return;
            var key = $"{flow}:{LocalPaths.BatchFolderName(w.EffStart, w.WindowEnd)}";
            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DraftlineDbContext>();
                var source = scope.ServiceProvider.GetRequiredService<IEbsPlmSource>();
                var store = scope.ServiceProvider.GetRequiredService<IServerBatchStore>();
                await IngestWindowAsync(db, source, store, flow, w.EffStart, w.WindowEnd, ct);
                _failStreak.Remove(key); // 成功即清零该窗失败计数
            }
            catch (Exception ex)
            {
                var streak = _failStreak[key] = _failStreak.GetValueOrDefault(key) + 1;
                if (streak >= StuckWindowRounds)
                    _logger.LogError(ex,
                        "⚠ 疑似永久坏窗：{Flow} 窗口 {Start:yyyy-MM-dd HH:mm}~{End:yyyy-MM-dd HH:mm} 已连续 {Streak} 轮失败，" +
                        "该流程更晚的数据被阻塞，请人工排查（EBS/网络/该时段数据）。",
                        flow, w.EffStart, w.WindowEnd, streak);
                else
                    _logger.LogError(ex,
                        "采集失败：{Flow} 窗口 {Start:yyyy-MM-dd HH:mm}~{End:yyyy-MM-dd HH:mm}（第 {Streak} 轮，本流程本轮停在此，下轮重试）",
                        flow, w.EffStart, w.WindowEnd, streak);
                break; // 停在失败窗口，避免 T_covered 越过留洞（队头阻塞是"绝不静默留洞"的代价）
            }
        }
    }

    /// <summary>
    /// 从已登记批次派生该流程的数据时间轴高水位 T_covered（不新增存储）。委托 <see cref="IngestionPlanner.HighWatermark"/>。
    /// </summary>
    private async Task<DateTime?> ComputeTCoveredAsync(DraftlineDbContext db, FlowType flow, string[] expectedGroups, CancellationToken ct)
    {
        var rows = await db.BatchRegistries
            .Where(b => b.Flow == flow)
            .Select(b => new { b.BatchId, b.GroupName })
            .ToListAsync(ct);
        return IngestionPlanner.HighWatermark(rows.Select(r => (r.BatchId, r.GroupName)), expectedGroups);
    }

    /// <summary>
    /// 取一个窗口的全部行（真实 EBS：一次调用），再按"数据自带的组别"拆分落盘：
    /// 核价按响应里的 GROUP_NAME 分组；挑图不分组，整批归到 "Center"。
    /// 组别不再由本服务预设（旧的写死 组1/组2 已移除），完全跟随源数据。
    /// </summary>
    private async Task IngestWindowAsync(DraftlineDbContext db, IEbsPlmSource source, IServerBatchStore store, FlowType flow, DateTime ws, DateTime we, CancellationToken ct)
    {
        var batchId = LocalPaths.BatchFolderName(ws, we);

        // 期望组别：核价是固定配置（组1/组2），挑图不分组只有 Center。
        // 即便某组当天没数据，也要为它建文件夹 + 空表，便于区分"采过但没数据"与"没采到"。
        var expectedGroups = flow == FlowType.Pricing ? _ebs.PricingGroups : new[] { "Center" };

        // 批次级预检：所有期望组都已登记、且所有已登记组的磁盘都在 → 跳过，避免对真实 EBS 重复取数。
        // 任一期望组缺登记，或任一已登记组磁盘缺失（存储自愈）→ 放行重取。
        var registered = await db.BatchRegistries.Where(b => b.Flow == flow && b.BatchId == batchId).ToListAsync(ct);
        var registeredGroups = registered.Select(b => b.GroupName).ToHashSet();
        var allExpectedRegistered = expectedGroups.All(g => registeredGroups.Contains(g));
        var allOnDisk = registered.All(b => Directory.Exists(LocalPaths.ServerBatchDir(store.Root, flow, b.GroupName, batchId)));
        if (registered.Count > 0 && allExpectedRegistered && allOnDisk)
            return;

        var rows = await source.FetchRowsAsync(flow, "SYSTEM", ws, we, ct);
        _logger.LogInformation("EBS 取数完成：{Flow} {Start:yyyy-MM-dd HH:mm}~{End:yyyy-MM-dd HH:mm} → {Count} 行",
            flow, ws, we, rows.Count);

        // 按组分桶：核价把数据行按"组N"前缀匹配到配置的完整组名（匹配不到的意外组按原始组名落盘）；挑图整批归 Center。
        Dictionary<string, IReadOnlyList<SourceRow>> byGroup;
        if (flow == FlowType.Pricing)
        {
            // 期望组名 → "组N"前缀的映射。若配置里两个组共用同一前缀（如都以"组1"开头），
            // 无法判断 EBS 里标"组1"的行该归到哪个，属配置歧义：首个胜出 + 告警，不让整批采集崩溃。
            var prefixToExpected = new Dictionary<string, string>();
            foreach (var g in expectedGroups)
            {
                var prefix = CanonicalGroup(g);
                if (!prefixToExpected.TryAdd(prefix, g))
                    _logger.LogWarning(
                        "Ebs:PricingGroups 存在重复的组别前缀 {Prefix}（{Kept} 与 {Dup}）；标该前缀的数据行将归到前者，请检查配置。",
                        prefix, prefixToExpected[prefix], g);
            }
            byGroup = rows
                .GroupBy(r =>
                {
                    var prefix = CanonicalGroup(r.GroupName);
                    return prefixToExpected.TryGetValue(prefix, out var full)
                        ? full
                        : (string.IsNullOrWhiteSpace(r.GroupName) ? prefix : r.GroupName!.Trim());
                })
                .ToDictionary(g => g.Key, g => (IReadOnlyList<SourceRow>)g.ToList());
        }
        else
        {
            byGroup = new Dictionary<string, IReadOnlyList<SourceRow>> { ["Center"] = rows };
        }

        // 期望组（即便空也建）∪ 数据里实际出现的组（含意外的组，避免丢数据）。
        foreach (var group in expectedGroups.Concat(byGroup.Keys).Distinct())
        {
            var groupRows = byGroup.TryGetValue(group, out var rs) ? rs : Array.Empty<SourceRow>();
            await PersistGroupAsync(db, store, flow, group, groupRows, ws, we, ct);
        }
    }

    /// <summary>把完整组名归一到"组N"前缀（如"组1（模组、先进激光…）"→"组1"），与固定期望组别对齐。无括号则原样去空白。</summary>
    private static string CanonicalGroup(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "未分组";
        var s = raw.Trim();
        var idx = s.IndexOfAny(new[] { '（', '(' });
        return idx > 0 ? s[..idx].Trim() : s;
    }

    private async Task PersistGroupAsync(DraftlineDbContext db, IServerBatchStore store, FlowType flow, string groupName, IReadOnlyList<SourceRow> rows, DateTime ws, DateTime we, CancellationToken ct)
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

        _logger.LogInformation("Ingesting/Healing batch: {Flow} - {GroupName} - {BatchId} (Disk:{Disk}, DB:{Db}, Rows:{Rows})",
            flow, groupName, batchId, diskExists, registry != null, rows.Count);

        // 注意：不再因 rows.Count==0 早退——空组也要落盘(文件夹+空表)并登记 TotalRows=0。

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
