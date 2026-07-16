using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Draftline.App.Controls;

/// <summary>
/// DataGrid 列表头的 Excel 风筛选器：表头显示"列名 + ▼"，点 ▼ 弹出"搜索框 + (全选) + 值勾选列表"。
/// 只影响显示（配合 DataGrid 的 ICollectionView.Filter），不改动底层数据与统计口径。
/// 由 <see cref="Views.BatchWorkView"/> 在动态建列时按可筛选列挂载。
/// </summary>
public partial class ColumnFilterHeader : UserControl
{
    private readonly ObservableCollection<FilterOption> _options = new();
    private readonly ICollectionView _optionsView;
    private bool _suppressSelectAll;

    /// <summary>当前筛选态：null = 未筛选（全选放行）；否则为"允许通过"的取值集合。</summary>
    private HashSet<string>? _active;

    public ColumnFilterHeader()
    {
        InitializeComponent();
        OptionList.ItemsSource = _options;
        _optionsView = CollectionViewSource.GetDefaultView(_options);
        _optionsView.Filter = FilterOptionBySearch;
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ColumnFilterHeader), new PropertyMetadata(string.Empty));

    /// <summary>列显示名（表头文本）。</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>提供该列的取值序列（下拉打开时实时读取，保证反映当前数据；内部会去重）。</summary>
    public Func<IEnumerable<string?>>? DistinctValuesProvider { get; set; }

    /// <summary>筛选变更后的回调（触发 DataGrid 视图刷新）。</summary>
    public Action? ApplyRequested { get; set; }

    /// <summary>该列是否处于筛选态。</summary>
    public bool IsActive => _active is not null;

    /// <summary>判定某行该列取值是否通过本列筛选（未筛选恒放行）。</summary>
    public bool Matches(string? raw) => _active is null || _active.Contains(Normalize(raw));

    private static string Normalize(string? raw) => (raw ?? string.Empty).Trim();

    // ▼ 打开：按当前数据重建候选值列表，并据当前筛选态还原勾选。
    private void OnDropDownOpened(object sender, RoutedEventArgs e) => Populate();

    private void Populate()
    {
        foreach (var o in _options) o.PropertyChanged -= OnOptionChanged;
        _options.Clear();

        var raws = DistinctValuesProvider?.Invoke() ?? Enumerable.Empty<string?>();
        foreach (var raw in raws.Select(Normalize).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal))
        {
            var opt = new FilterOption(raw) { IsChecked = _active is null || _active.Contains(raw) };
            opt.PropertyChanged += OnOptionChanged;
            _options.Add(opt);
        }

        SearchBox.Text = string.Empty;
        _optionsView.Refresh();
        UpdateSelectAllState();
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterOption.IsChecked)) UpdateSelectAllState();
    }

    private bool FilterOptionBySearch(object item)
    {
        var text = SearchBox?.Text;
        if (string.IsNullOrEmpty(text)) return true;
        return item is FilterOption o && o.Display.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _optionsView.Refresh();

    // 三态"(全选)"：仅作用于当前搜索可见项；勾选联动由 UpdateSelectAllState 反算。
    private void UpdateSelectAllState()
    {
        _suppressSelectAll = true;
        var all = _options.Count > 0 && _options.All(o => o.IsChecked);
        var none = _options.All(o => !o.IsChecked);
        SelectAll.IsChecked = all ? true : none ? false : (bool?)null;
        _suppressSelectAll = false;
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectAll) return;
        var target = SelectAll.IsChecked == true; // 三态点击后可能为 null，规整为 false
        SelectAll.IsChecked = target;
        foreach (FilterOption o in _optionsView) o.IsChecked = target;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        var chk = _options.Where(o => o.IsChecked).Select(o => o.Raw).ToHashSet(StringComparer.Ordinal);
        // 全勾 = 未筛选（null）；否则记下允许通过的取值集合（全不勾则为空集，隐藏所有行，与 Excel 一致）。
        _active = chk.Count == _options.Count ? null : chk;
        UpdateActiveVisual();
        FilterToggle.IsChecked = false; // 关闭下拉
        ApplyRequested?.Invoke();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _active = null;
        foreach (var o in _options) o.IsChecked = true;
        UpdateSelectAllState();
        UpdateActiveVisual();
        FilterToggle.IsChecked = false;
        ApplyRequested?.Invoke();
    }

    private void UpdateActiveVisual()
    {
        Glyph.Foreground = IsActive ? (Brush)FindResource("Accent") : (Brush)FindResource("TextMuted");
        Glyph.FontWeight = IsActive ? FontWeights.Bold : FontWeights.Normal;
        FilterToggle.ToolTip = IsActive ? "已筛选（点击可修改）" : "筛选";
    }
}
