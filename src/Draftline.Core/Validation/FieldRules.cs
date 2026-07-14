using System.Globalization;
using Draftline.Core.Enums;
using Draftline.Core.Models;

namespace Draftline.Core.Validation;

/// <summary>
/// 待填列的取值校验（回传前把关）。目前只有数值列（核价目标价）有格式规则：
/// 必须是有效数字、大于 0、最多两位小数。非法值返回中文错误原因，合法返回 null。
/// </summary>
public static class FieldRules
{
    /// <summary>校验单个字段值。返回错误原因（中文）或 null（合法/无需校验）。</summary>
    public static string? Validate(FieldDefinition field, string? value) => field.Editor switch
    {
        FieldEditor.Number => ValidateNumber(field.DisplayName, value),
        _ => null,
    };

    /// <summary>
    /// 数值列校验：空值交由「必填」校验处理，这里只在填了值时把关格式。
    /// 允许可选正负号 + 小数点，不接受千分位/科学计数；要求大于 0、小数位 ≤ 2。
    /// </summary>
    public static string? ValidateNumber(string display, string? value)
    {
        var s = value?.Trim() ?? string.Empty;
        if (s.Length == 0) return null; // 空由必填校验处理

        if (!decimal.TryParse(
                s,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var number))
            return $"{display}必须是有效数字。";

        if (number <= 0) return $"{display}必须大于 0。";

        var dot = s.IndexOf('.');
        if (dot >= 0 && s.Length - dot - 1 > 2) return $"{display}最多保留两位小数。";

        return null;
    }
}
