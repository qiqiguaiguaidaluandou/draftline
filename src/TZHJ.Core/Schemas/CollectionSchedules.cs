using TZHJ.Core.Enums;
using TZHJ.Core.Models;

namespace TZHJ.Core.Schemas;

/// <summary>
/// 默认采集时间窗（来自方案设计 §批次模型）。两流程各一套、可配置，将来由 IConfigGateway 下发覆盖。
/// 铺满 24h、首尾相接、批次键 =(流程+窗口起止) 防重复。
/// </summary>
public static class CollectionSchedules
{
    /// <summary>核价：2 窗，日界 15:30 / 15:31。</summary>
    public static IReadOnlyList<CollectionWindow> Pricing { get; } = new[]
    {
        new CollectionWindow
        {
            Name = "上午批", Flow = FlowType.Pricing,
            StartDayOffset = -1, StartTime = new TimeOnly(15, 31), EndTime = new TimeOnly(9, 30),
        },
        new CollectionWindow
        {
            Name = "下午批", Flow = FlowType.Pricing,
            StartDayOffset = 0, StartTime = new TimeOnly(9, 31), EndTime = new TimeOnly(15, 30),
        },
    };

    /// <summary>挑图：3 窗，日界 18:00 / 18:01（比核价多一傍晚窗）。</summary>
    public static IReadOnlyList<CollectionWindow> DrawingSelection { get; } = new[]
    {
        new CollectionWindow
        {
            Name = "夜间批", Flow = FlowType.DrawingSelection,
            StartDayOffset = -1, StartTime = new TimeOnly(18, 1), EndTime = new TimeOnly(9, 30),
        },
        new CollectionWindow
        {
            Name = "上午批", Flow = FlowType.DrawingSelection,
            StartDayOffset = 0, StartTime = new TimeOnly(9, 31), EndTime = new TimeOnly(15, 30),
        },
        new CollectionWindow
        {
            Name = "下午批", Flow = FlowType.DrawingSelection,
            StartDayOffset = 0, StartTime = new TimeOnly(15, 31), EndTime = new TimeOnly(18, 0),
        },
    };

    public static IReadOnlyList<CollectionWindow> For(FlowType flow) =>
        flow == FlowType.Pricing ? Pricing : DrawingSelection;
}
