using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using Draftline.App.Controls;
using Draftline.App.ViewModels;
using Draftline.Core.Enums;
using Draftline.Core.Models;

namespace Draftline.App.Views;

public partial class BatchWorkView : UserControl
{
    private bool _columnsBuilt;

    // 已挂表头筛选（▼）的列：列键 + 其筛选器，供行筛选谓词逐列判定。
    private readonly List<(string Key, ColumnFilterHeader Header)> _filters = new();
    private ICollectionView? _view;

    public BatchWorkView() => InitializeComponent();

    // 仅负责按字段 schema 动态建列（视图职责）；行数据加载由 NavigationService 在导航时触发。
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BatchWorkViewModel vm) return;
        if (!_columnsBuilt)
        {
            BuildColumns(vm);

            // 行显示筛选：只影响可见性，不改动 Rows 本身（统计/提交闸门仍按全部行）。
            _view = CollectionViewSource.GetDefaultView(vm.Rows);
            _view.Filter = RowPassesFilters;

            _columnsBuilt = true;
        }
    }

    private bool RowPassesFilters(object item)
    {
        if (item is not RowViewModel r) return true;
        foreach (var (key, header) in _filters)
            if (!header.Matches(r[key])) return false;
        return true;
    }

    private void ApplyFilters() => _view?.Refresh();

    /// <summary>列由字段 schema 驱动：只读字段 / 手填文本 / 手填下拉 + 行状态 + 操作（图纸不在表单内展示，到文件夹查看）。</summary>
    private void BuildColumns(BatchWorkViewModel vm)
    {
        Grid.Columns.Clear();
        _filters.Clear();

        foreach (var f in vm.Fields.OrderBy(x => x.Order))
        {
            var col = BuildFieldColumn(f, vm.IsReadOnly);
            if (vm.FilterableKeys.Contains(f.Key))
                AttachFilter(col, f, vm);
            Grid.Columns.Add(col);
        }

        Grid.Columns.Add(BuildStatusColumn());
        Grid.Columns.Add(ReadOnlyColumn("异常原因", nameof(RowViewModel.ExceptionReason), 120));

        if (!vm.IsReadOnly)
            Grid.Columns.Add(BuildActionColumn());
    }

    // 给列挂 Excel 风表头筛选（▼）：表头改为"列名 + ▼"，候选值在下拉打开时按当前数据实时取。
    // 关闭该列点表头排序，避免与 ▼ 交互混淆（需求为"只要筛选"）。
    private void AttachFilter(DataGridColumn col, FieldDefinition f, BatchWorkViewModel vm)
    {
        var key = f.Key;
        var header = new ColumnFilterHeader
        {
            Title = f.DisplayName,
            DistinctValuesProvider = () => vm.Rows.Select(r => r[key]),
            ApplyRequested = ApplyFilters,
        };
        col.Header = header;
        col.CanUserSort = false;
        _filters.Add((key, header));
    }

    private static DataGridColumn BuildFieldColumn(FieldDefinition f, bool readOnly)
    {
        // 已处理批次只读查看：所有列只读。
        // 手填下拉：单元格内常驻一个 ComboBox（单击即选，无需双击进编辑态），样式见 FluentTheme。
        if (!readOnly && f.IsEditable && f.Editor == FieldEditor.Dropdown)
        {
            var combo = new FrameworkElementFactory(typeof(ComboBox));
            combo.SetValue(ItemsControl.ItemsSourceProperty, f.Options);
            combo.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 4, 2, 4));
            combo.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            combo.SetBinding(ComboBox.SelectedItemProperty, new Binding($"[{f.Key}]")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
            return new DataGridTemplateColumn
            {
                Header = f.DisplayName,
                CellTemplate = new DataTemplate { VisualTree = combo },
                Width = 150,
            };
        }

        // 手填文本（如目标价）：单元格内常驻 TextBox（单击即填，无需双击进编辑态）。
        if (!readOnly && f.IsEditable)
        {
            var box = new FrameworkElementFactory(typeof(TextBox));
            box.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 6, 2, 6));
            box.SetValue(Control.PaddingProperty, new Thickness(6, 3, 6, 3)); // 收紧内边距，给数值更多可见宽度
            // 垂直撑满单元格（文字由样式 VerticalContentAlignment=Center 居中），避免按内容高度时被上下切掉
            box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
            box.SetBinding(TextBox.TextProperty, new Binding($"[{f.Key}]")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
            return new DataGridTemplateColumn
            {
                Header = f.DisplayName,
                CellTemplate = new DataTemplate { VisualTree = box },
                Width = 160,
            };
        }

        return new DataGridTextColumn
        {
            Header = f.DisplayName,
            Binding = new Binding($"[{f.Key}]"),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
        };
    }

    /// <summary>行状态列：彩色徽章（色键 StatusKind → 浅底 + 深色文字），一眼区分待处理/已处理/挂起异常。</summary>
    private static DataGridTemplateColumn BuildStatusColumn()
    {
        const string xaml =
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
            "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
            "<Border CornerRadius='11' Padding='9,2' HorizontalAlignment='Left' VerticalAlignment='Center' " +
            "Background='{Binding StatusKind, Converter={StaticResource StatusKindToBg}}'>" +
            "<TextBlock Text='{Binding StatusText}' FontSize='12' FontWeight='SemiBold' " +
            "Foreground='{Binding StatusKind, Converter={StaticResource StatusKindToBrush}}'/>" +
            "</Border></DataTemplate>";

        return new DataGridTemplateColumn
        {
            Header = "行状态",
            CellTemplate = (DataTemplate)XamlReader.Parse(xaml),
            Width = 96,
        };
    }

    private static DataGridTextColumn ReadOnlyColumn(string header, string path, double width)
    {
        var col = new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            IsReadOnly = true,
            Width = width,
        };
        // 与列表/异常池一致：过长内容省略号截断，鼠标悬停显示完整文本（如异常原因）。
        if (Application.Current?.TryFindResource("CellTextEllipsisToolTip") is Style ellipsis)
            col.ElementStyle = ellipsis;
        return col;
    }

    /// <summary>操作列：非异常显示「挂起异常」，异常显示「撤销异常」（可见性由行状态驱动）；
    /// 「重新获取图纸」常驻——任何行都可按料号重取一次图纸（同名覆盖 / 无则新增）。</summary>
    private static DataGridTemplateColumn BuildActionColumn()
    {
        const string xaml =
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
            "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
            "<StackPanel Orientation='Horizontal'>" +
            "<Button Content='挂起异常' Padding='8,3' MinHeight='26' " +
            "Visibility='{Binding SuspendVisibility}' " +
            "Command='{Binding DataContext.MarkExceptionCommand, RelativeSource={RelativeSource AncestorType={x:Type UserControl}}}' " +
            "CommandParameter='{Binding}'/>" +
            "<Button Content='撤销异常' Padding='8,3' MinHeight='26' " +
            "Visibility='{Binding RestoreVisibility}' " +
            "Command='{Binding DataContext.RestoreCommand, RelativeSource={RelativeSource AncestorType={x:Type UserControl}}}' " +
            "CommandParameter='{Binding}'/>" +
            "<Button Content='重新获取图纸' Padding='8,3' MinHeight='26' Margin='6,0,0,0' " +
            "Command='{Binding DataContext.RefetchDrawingCommand, RelativeSource={RelativeSource AncestorType={x:Type UserControl}}}' " +
            "CommandParameter='{Binding}'/>" +
            "</StackPanel></DataTemplate>";

        return new DataGridTemplateColumn
        {
            Header = "操作",
            CellTemplate = (DataTemplate)XamlReader.Parse(xaml),
            Width = 240,
        };
    }
}
