using Draftline.Core.Enums;
using Draftline.Core.Schemas;

namespace Draftline.Tests.Schemas;

/// <summary>
/// 取数正确性核心决策（docs/changes/023，D 方法 T_covered）：
/// 高水位派生、改窗不倒补、切换不丢数、宕机补采出标准批次、追赶上限、首次部署。
/// </summary>
public class IngestionPlannerTests
{
    // 现状核价排程（未改窗）。上午批 [D-1 15:31→D 09:30] 触发10:00；下午批 [D 09:31→D 15:30] 触发16:00。
    private static IReadOnlyList<IngestionSchedule> PricingStd => IngestionSchedules.For(FlowType.Pricing).ToList();

    private static DateTime Dt(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0);

    // ---- 高水位派生 ----

    [Fact]
    public void HighWatermark_takes_latest_end_of_fully_registered_batches()
    {
        var expected = new[] { "A", "B" };
        var reg = new (string, string)[]
        {
            ("20260527_0931-1530", "A"), ("20260527_0931-1530", "B"), // 齐全 → we=05-27 15:30
            ("20260526_0931-1530", "A"), ("20260526_0931-1530", "B"), // 齐全 → we=05-26 15:30
        };

        Assert.Equal(Dt(2026, 5, 27, 15, 30), IngestionPlanner.HighWatermark(reg, expected));
    }

    [Fact]
    public void HighWatermark_ignores_partial_group_batches()
    {
        // 05-28 那批只登记了 A（组2 部分失败）→ 不计入 → 高水位停在 05-27，留给逐组预检自愈。
        var expected = new[] { "A", "B" };
        var reg = new (string, string)[]
        {
            ("20260527_0931-1530", "A"), ("20260527_0931-1530", "B"),
            ("20260528_0931-1530", "A"),
        };

        Assert.Equal(Dt(2026, 5, 27, 15, 30), IngestionPlanner.HighWatermark(reg, expected));
    }

    [Fact]
    public void HighWatermark_null_when_no_qualified_batch()
    {
        Assert.Null(IngestionPlanner.HighWatermark(Array.Empty<(string, string)>(), new[] { "A", "B" }));
        // 无法解析的目录名忽略。
        Assert.Null(IngestionPlanner.HighWatermark(new[] { ("garbage", "A") }, new[] { "A" }));
    }

    // ---- 规划 ----

    [Fact]
    public void Steady_state_plans_standard_windows_identical_to_schedule()
    {
        // 稳态：高水位在昨天下午批止，今天两批已到点。应产出与排程逐字一致的标准窗口（effStart==ws）。
        var now = Dt(2026, 5, 28, 17, 0);
        var tCovered = Dt(2026, 5, 27, 15, 30);

        var plan = IngestionPlanner.Plan(now, tCovered, horizonDays: 2, PricingStd);

        Assert.Equal(2, plan.Count);
        Assert.Equal(Dt(2026, 5, 27, 15, 31), plan[0].EffStart); // 今天上午批标准起
        Assert.Equal(Dt(2026, 5, 28, 9, 30), plan[0].WindowEnd);
        Assert.Equal(Dt(2026, 5, 28, 9, 31), plan[1].EffStart);  // 今天下午批标准起
        Assert.Equal(Dt(2026, 5, 28, 15, 30), plan[1].WindowEnd);
    }

    [Fact]
    public void Nothing_to_do_when_watermark_at_latest()
    {
        var now = Dt(2026, 5, 28, 17, 0);
        var plan = IngestionPlanner.Plan(now, tCovered: Dt(2026, 5, 28, 15, 30), horizonDays: 2, PricingStd);
        Assert.Empty(plan);
    }

    [Fact]
    public void No_backfill_and_no_loss_when_window_end_extended()
    {
        // 改窗（闭环）：下午批止 15:30→15:59、上午批起 15:31→16:00；D 日 20:00 部署，旧下午批已采到 15:30。
        var changed = new[]
        {
            new IngestionSchedule { Flow = FlowType.Pricing, Name = "上午批", TriggerTime = new(10, 0), StartDayOffset = -1, StartTime = new(16, 0), EndTime = new(9, 30) },
            new IngestionSchedule { Flow = FlowType.Pricing, Name = "下午批", TriggerTime = new(16, 0), StartDayOffset = 0,  StartTime = new(9, 31), EndTime = new(15, 59) },
        };
        var now = Dt(2026, 5, 28, 20, 0);
        var tCovered = Dt(2026, 5, 28, 15, 30);

        var plan = IngestionPlanner.Plan(now, tCovered, horizonDays: 2, changed);

        // 只补被挪出的碎片 [15:31, 15:59]：不倒补 09:31–15:30，也不丢 15:31–15:59。
        Assert.Single(plan);
        Assert.Equal(Dt(2026, 5, 28, 15, 31), plan[0].EffStart);
        Assert.Equal(Dt(2026, 5, 28, 15, 59), plan[0].WindowEnd);
    }

    [Fact]
    public void Downtime_backfills_standard_batches_no_elongated_window()
    {
        // 高水位落后 2 天，horizon=2 → 补出的都是逐个标准窗口（effStart==ws），无跨天拉长窗。
        var now = Dt(2026, 5, 28, 17, 0);
        var tCovered = Dt(2026, 5, 26, 15, 30);

        var plan = IngestionPlanner.Plan(now, tCovered, horizonDays: 2, PricingStd);

        Assert.Equal(4, plan.Count);
        var expectedStarts = new[]
        {
            Dt(2026, 5, 26, 15, 31), Dt(2026, 5, 27, 9, 31),
            Dt(2026, 5, 27, 15, 31), Dt(2026, 5, 28, 9, 31),
        };
        for (int i = 0; i < plan.Count; i++)
        {
            Assert.Equal(expectedStarts[i], plan[i].EffStart);
            // 每批都是标准窗口：起点即排程窗口起，绝不跨天。
            Assert.True(plan[i].WindowEnd - plan[i].EffStart <= TimeSpan.FromHours(24));
        }
    }

    [Fact]
    public void Catchup_capped_by_horizon_drops_older_data_keeps_standard_batch()
    {
        // 高水位落后 5 天但 horizon=2：只补 05-27/05-28，最早窗口仍是标准起点（丢弃更久远数据，不产出拉长窗）。
        var now = Dt(2026, 5, 28, 17, 0);
        var tCovered = Dt(2026, 5, 23, 15, 30);

        var plan = IngestionPlanner.Plan(now, tCovered, horizonDays: 2, PricingStd);

        Assert.Equal(4, plan.Count);
        // 最早候选是 05-27 上午批，其标准起点 05-26 15:31——不是 tCovered+1（05-23 15:31）。
        Assert.Equal(Dt(2026, 5, 26, 15, 31), plan[0].EffStart);
    }

    [Fact]
    public void First_deploy_uses_window_start_like_current_behavior()
    {
        var now = Dt(2026, 5, 28, 17, 0);
        var plan = IngestionPlanner.Plan(now, tCovered: null, horizonDays: 2, PricingStd);

        // 无高水位 → 全部用标准窗口起点，等同现状"回看昨天+今天"。
        Assert.Equal(4, plan.Count);
        // 最早候选是 05-27 上午批，其标准起点跨到前一天 05-26 15:31（StartDayOffset=-1）。
        Assert.Equal(Dt(2026, 5, 26, 15, 31), plan[0].EffStart);
        Assert.True(plan.All(p => p.WindowEnd - p.EffStart <= TimeSpan.FromHours(24))); // 全是标准窗口
    }

    [Fact]
    public void Horizon_one_looks_at_today_only()
    {
        // horizonDays=1 → 只看今天（d=0）；昨天的窗口不进候选。
        var now = Dt(2026, 5, 28, 17, 0);
        var tCovered = Dt(2026, 5, 27, 15, 30);

        var plan = IngestionPlanner.Plan(now, tCovered, horizonDays: 1, PricingStd);

        Assert.Equal(2, plan.Count);
        Assert.All(plan, p => Assert.Equal(new DateOnly(2026, 5, 28), DateOnly.FromDateTime(p.WindowEnd))); // 窗口止都在今天
    }

    [Fact]
    public void Horizon_zero_plans_nothing_hence_the_caller_clamps_to_one()
    {
        // horizonDays=0 → 枚举不到任何窗口 → 空计划（静默停采）。调用方 CheckSchedulesAsync 因此 clamp 下限为 1。
        var now = Dt(2026, 5, 28, 17, 0);
        Assert.Empty(IngestionPlanner.Plan(now, tCovered: null, horizonDays: 0, PricingStd));
    }

    [Fact]
    public void Windows_before_trigger_time_are_not_planned()
    {
        // 今天 09:00：两批触发点(10:00/16:00)都没到 → 今天不产出；只剩昨天已到点的。
        var now = Dt(2026, 5, 28, 9, 0);
        var tCovered = Dt(2026, 5, 27, 15, 30);

        var plan = IngestionPlanner.Plan(now, tCovered, horizonDays: 2, PricingStd);
        Assert.Empty(plan); // 昨天的都在高水位内，今天的还没到点
    }
}
