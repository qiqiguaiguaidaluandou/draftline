using System;
using System.Collections.Generic;
using System.Linq;
using Draftline.Core.Models;

namespace Draftline.Core.Schemas;

/// <summary>
/// 采集"取数正确性"的纯决策逻辑（见 docs/changes/023）。不含 DB / EBS / 磁盘副作用，便于单测。
///
/// 核心是「数据时间轴高水位 T_covered」模型：每流程记"已连续采到哪个时刻"，取数从 T_covered 之后接续，
/// 从而改窗不倒补、切换不丢数；且因排程无缝平铺，日常/宕机补采都产出标准窗口批次。
/// </summary>
public static class IngestionPlanner
{
    /// <summary>本轮计划采集的一个窗口：实际取数起点（可能因改窗延长而 &gt; 排程窗口起）与窗口止。</summary>
    public readonly record struct PlannedWindow(DateTime EffStart, DateTime WindowEnd);

    /// <summary>
    /// 从已登记批次派生该流程的高水位 T_covered（不新增存储）。
    /// batchId 编码了窗口止（<see cref="LocalPaths.TryParseFolderName"/>），取"期望组已登记齐全"的 batchId 里最晚的窗口止。
    /// 只登记了部分组的批次（分组部分失败）不计入 → T_covered 不越过它 → 留给逐组预检自愈。
    /// 无任何合格批次返回 null（首次部署 / 长期闲置）。
    /// </summary>
    public static DateTime? HighWatermark(
        IEnumerable<(string BatchId, string GroupName)> registered,
        IReadOnlyCollection<string> expectedGroups)
    {
        DateTime? tc = null;
        foreach (var g in registered.GroupBy(x => x.BatchId))
        {
            var groups = g.Select(x => x.GroupName).ToHashSet();
            if (!expectedGroups.All(groups.Contains)) continue;                 // 组不齐 → 不算采完
            if (!LocalPaths.TryParseFolderName(g.Key, out _, out var we)) continue;
            if (tc is null || we > tc) tc = we;
        }
        return tc;
    }

    /// <summary>
    /// 算出本轮应采集的窗口序列（按窗口止升序），已过"到点 + 未覆盖"两道闸门。
    /// 假设逐窗成功、T_covered 递推推进：实际执行时若某窗失败应就地停止（不推进），下一轮以真实 T_covered 重算。
    /// </summary>
    /// <param name="now">采集用"当前时间"（含日期）。</param>
    /// <param name="tCovered">该流程当前高水位；null = 首次/闲置。</param>
    /// <param name="horizonDays">回看日历天数（含今天）。默认调用方给 2 = 昨天+今天，对齐现状。</param>
    /// <param name="schedules">该流程的排程（调用方已按 flow 过滤）。</param>
    public static IReadOnlyList<PlannedWindow> Plan(
        DateTime now, DateTime? tCovered, int horizonDays, IEnumerable<IngestionSchedule> schedules)
    {
        var scheduleList = schedules.Where(s => s.Enabled).ToList();

        // 回看 horizonDays 个日历天（含今天），枚举每一个已到触发时刻的【标准】窗口。
        var candidates = new List<(DateTime ws, DateTime we)>();
        for (int d = horizonDays - 1; d >= 0; d--)   // horizonDays=2 → d=1,0 → 昨天、今天
        {
            var triggerDate = now.Date.AddDays(-d);
            var anchor = DateOnly.FromDateTime(triggerDate);
            foreach (var s in scheduleList)
            {
                if (now < triggerDate.Add(s.TriggerTime.ToTimeSpan())) continue; // 没到点不采（不取未封口窗口）
                var (ws, we) = s.ToWindow().Resolve(anchor);
                candidates.Add((ws, we));
            }
        }
        candidates.Sort((a, b) => a.we.CompareTo(b.we)); // 按窗口止升序，保证 T_covered 逐窗正确推进

        var plan = new List<PlannedWindow>();
        var tc = tCovered;
        foreach (var (ws, we) in candidates)
        {
            if (tc is DateTime t0 && we <= t0) continue;                         // 已覆盖 → 跳过（不倒补）
            var effStart = tc is DateTime t && t.AddMinutes(1) > ws
                ? t.AddMinutes(1)   // 与已采数据重叠(改窗把窗口止往后延) → 从接续点起，不倒补
                : ws;               // 正常/宕机 → 标准窗口起点
            plan.Add(new PlannedWindow(effStart, we));
            tc = we;                                                             // happy-path 推进
        }
        return plan;
    }
}
