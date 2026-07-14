using Draftline.Core.Enums;
using Draftline.Core.Models;
using Draftline.Core.Schemas;
using Draftline.Core.Validation;

namespace Draftline.Tests.Validation;

/// <summary>
/// 目标价（核价手填数值列）回传前校验：有效数字、大于 0、最多两位小数。
/// 空值不在此拦（交由「必填」闸门），故合法。
/// </summary>
public class FieldRulesTests
{
    private static FieldDefinition TargetPrice =>
        FieldSchemas.Pricing.Single(f => f.Key == FieldSchemas.PricingKeys.TargetPrice);

    [Theory]
    [InlineData("10")]
    [InlineData("10.5")]
    [InlineData("10.55")]
    [InlineData("0.01")]
    [InlineData("1234567.89")]
    [InlineData(" 10.50 ")] // 前后空白容忍
    [InlineData("")]        // 空由必填校验处理，格式校验放行
    [InlineData("   ")]
    [InlineData(null)]
    public void Valid_values_pass(string? value)
    {
        Assert.Null(FieldRules.Validate(TargetPrice, value));
    }

    [Theory]
    [InlineData("abc")]        // 非数字
    [InlineData("10a")]        // 混入字母
    [InlineData("1,000")]      // 千分位不接受
    [InlineData("1e3")]        // 科学计数不接受
    [InlineData("10.555")]     // 超过两位小数
    [InlineData("0.001")]
    [InlineData("0")]          // 不大于 0
    [InlineData("-5")]         // 负数
    [InlineData("-0.5")]
    public void Invalid_values_are_rejected(string value)
    {
        Assert.NotNull(FieldRules.Validate(TargetPrice, value));
    }

    [Fact]
    public void ReadOnly_and_dropdown_fields_are_not_number_checked()
    {
        // 非数值列不套用数值规则（如挑图下拉、EBS 只读列）。
        var dropdown = FieldSchemas.DrawingSelection.Single(f => f.Editor == FieldEditor.Dropdown);
        Assert.Null(FieldRules.Validate(dropdown, "任意文本"));

        var readOnly = FieldSchemas.Pricing.First(f => f.Editor == FieldEditor.ReadOnly);
        Assert.Null(FieldRules.Validate(readOnly, "abc"));
    }
}
