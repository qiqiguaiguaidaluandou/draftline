namespace TZHJ.Core.Enums;

/// <summary>
/// 业务流程类型。两流程严格隔离：各自的 EBS 取数、批次、表单字段、时间窗、回传目标，数据互不可见。
/// </summary>
public enum FlowType
{
    /// <summary>图纸核价：操作员手填目标价 → 整批回传 SRM。</summary>
    Pricing,

    /// <summary>挑图纸：操作员填"是否机加中心可以做" → 整批回传 EBS。</summary>
    DrawingSelection,
}
