using CommunityToolkit.Mvvm.ComponentModel;

namespace Draftline.App.Controls;

/// <summary>
/// 列筛选下拉里的一个候选值（Excel 风"值列表 + 勾选"）。
/// <see cref="Raw"/> 为归一化后的实际取值（空值归一为 ""）；<see cref="Display"/> 供界面显示（空值显示"(空)"）。
/// </summary>
public sealed partial class FilterOption : ObservableObject
{
    public FilterOption(string raw) => Raw = raw;

    /// <summary>归一化后的实际取值（用于与行取值比对；空值为空串）。</summary>
    public string Raw { get; }

    /// <summary>界面显示文本（空值显示为"(空)"）。</summary>
    public string Display => Raw.Length == 0 ? "(空)" : Raw;

    [ObservableProperty] private bool _isChecked = true;
}
