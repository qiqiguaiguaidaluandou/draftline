using Draftline.Core.Enums;
using Draftline.Core.Schemas;

namespace Draftline.Core.Logging;

/// <summary>
/// 日志显示词表：动作英文值 → 中文名、payload 片段美化。后台管理（全量审计）与
/// 操作员「我的操作」共用同一套词表，避免两处翻译走偏。
/// </summary>
public static class LogText
{
    /// <summary>动作值（写入 ActivityLogs 的英文）→ 中文显示名。既驱动后台筛选下拉，也用于列表渲染。</summary>
    public static readonly (string Value, string Label)[] Actions =
    {
        (LogActions.Login, "用户登录"),
        (LogActions.AdminLogin, "后台登录"),
        (LogActions.ChangePassword, "修改密码"),
        (LogActions.UpdateRow, "修改数据行"),
        (LogActions.Suspend, "挂起异常"),
        (LogActions.Resolve, "处理异常"),
        (LogActions.RefetchDrawing, "重新获取图纸"),
        (LogActions.Submit, "提交回传"),
        (LogActions.Ingest, "数据导入"),
        (LogActions.AdminCreateUser, "新建用户"),
        (LogActions.AdminResetPassword, "重置密码"),
        (LogActions.AdminSetActive, "启用/停用用户"),
        (LogActions.AdminSetUserRoles, "分配角色"),
        (LogActions.AdminCreateRole, "新建角色"),
        (LogActions.AdminUpdateRole, "编辑角色"),
        (LogActions.AdminDeleteRole, "删除角色"),
    };

    /// <summary>
    /// 操作员「我的操作」可见的动作：本人的业务操作 + 登录/改密。
    /// 刻意排除系统导入(Ingest)、后台管理动作(Admin*)——那些属于全量审计，只在后台看。
    /// </summary>
    public static readonly string[] OperatorActions =
    {
        LogActions.Submit, LogActions.UpdateRow, LogActions.Suspend, LogActions.Resolve,
        LogActions.RefetchDrawing, LogActions.Login, LogActions.ChangePassword,
    };

    /// <summary>动作英文值 → 中文；未知动作原样返回，避免吞掉新增类型。</summary>
    public static string ActionLabel(string action)
    {
        foreach (var (val, label) in Actions)
            if (val == action) return label;
        return action;
    }

    /// <summary>流程中文名。</summary>
    public static string FlowLabel(FlowType flow) => flow == FlowType.Pricing ? "核价" : "挑图";

    /// <summary>回传目标：核价→SRM，挑图→EBS（与 /submit 端点一致）。</summary>
    public static string SubmitTarget(FlowType? flow) => (flow ?? FlowType.Pricing) == FlowType.Pricing ? "SRM" : "EBS";

    /// <summary>提交回传的动作显示名：区分「提交回传/重新回传」并带上回传目标（如「提交回传 → SRM」）。</summary>
    public static string SubmitLabel(FlowType? flow, bool isRetry) =>
        $"{(isRetry ? "重新回传" : "提交回传")} → {SubmitTarget(flow)}";

    /// <summary>该条 Submit 日志是否为「重新回传」（异常行重传）。以 payload 里的标记判定。</summary>
    public static bool IsResubmit(string? payload) => payload is not null && payload.Contains("重新回传");

    /// <summary>
    /// 物料可读标签：编码 + 名称（尽力从行值里取，缺失字段自动省略；都取不到则回退 rowKey）。
    /// 供各业务日志展示「哪个物料」，而非仅内部行键。
    /// </summary>
    public static string MaterialLabel(FlowType flow, string rowKey, IReadOnlyDictionary<string, string?>? values)
    {
        if (values is null) return rowKey;
        var codeKey = flow == FlowType.Pricing ? FieldSchemas.PricingKeys.MaterialCode : FieldSchemas.DrawingKeys.MaterialCode;
        var nameKey = flow == FlowType.Pricing ? FieldSchemas.PricingKeys.Name : FieldSchemas.DrawingKeys.MaterialDesc;
        var parts = new List<string>();
        if (values.TryGetValue(codeKey, out var code) && !string.IsNullOrWhiteSpace(code)) parts.Add(code!.Trim());
        if (values.TryGetValue(nameKey, out var name) && !string.IsNullOrWhiteSpace(name)) parts.Add(name!.Trim());
        return parts.Count > 0 ? string.Join(" ", parts) : rowKey;
    }

    /// <summary>
    /// 行改动摘要：仅列出**真正变化**的字段，用中文列名 + 老值→新值（如「目标价(10→12)」）。
    /// 遍历 <paramref name="oldValues"/>（store 实际改写过的字段）以避免把未落列的键算作改动；无变化返回空串。
    /// </summary>
    public static string DescribeChanges(FlowType flow, IReadOnlyDictionary<string, string?> oldValues, IReadOnlyDictionary<string, string?> newValues)
    {
        var labels = FieldSchemas.For(flow).ToDictionary(f => f.Key, f => f.DisplayName);
        var parts = new List<string>();
        foreach (var (key, ov) in oldValues)
        {
            newValues.TryGetValue(key, out var nv);
            var o = (ov ?? "").Trim();
            var n = (nv ?? "").Trim();
            if (o == n) continue;
            var label = labels.TryGetValue(key, out var d) ? d : key;
            parts.Add($"{label}({(o.Length == 0 ? "空" : o)}→{(n.Length == 0 ? "空" : n)})");
        }
        return string.Join("、", parts);
    }
}
