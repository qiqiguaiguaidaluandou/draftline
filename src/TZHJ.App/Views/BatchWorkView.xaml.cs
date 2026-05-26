using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using TZHJ.App.ViewModels;
using TZHJ.Core.Enums;
using TZHJ.Core.Models;

namespace TZHJ.App.Views;

public partial class BatchWorkView : UserControl
{
    private bool _columnsBuilt;

    public BatchWorkView() => InitializeComponent();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BatchWorkViewModel vm) return;
        if (!_columnsBuilt)
        {
            BuildColumns(vm);
            _columnsBuilt = true;
        }
        await vm.LoadAsync();
    }

    /// <summary>列由字段 schema 驱动：只读字段 / 手填文本 / 手填下拉 + 图纸 + 行状态 + 操作。</summary>
    private void BuildColumns(BatchWorkViewModel vm)
    {
        Grid.Columns.Clear();

        foreach (var f in vm.Fields.OrderBy(x => x.Order))
            Grid.Columns.Add(BuildFieldColumn(f, vm.IsReadOnly));

        Grid.Columns.Add(ReadOnlyColumn("图纸", nameof(RowViewModel.DrawingText), 90));
        Grid.Columns.Add(ReadOnlyColumn("行状态", nameof(RowViewModel.StatusText), 90));
        Grid.Columns.Add(ReadOnlyColumn("异常原因", nameof(RowViewModel.ExceptionReason), 120));

        if (!vm.IsReadOnly)
            Grid.Columns.Add(BuildActionColumn());
    }

    private static DataGridColumn BuildFieldColumn(FieldDefinition f, bool readOnly)
    {
        // 已处理批次只读查看：所有列只读。
        if (!readOnly && f.IsEditable && f.Editor == FieldEditor.Dropdown)
        {
            return new DataGridComboBoxColumn
            {
                Header = f.DisplayName,
                ItemsSource = f.Options,
                SelectedItemBinding = new Binding($"[{f.Key}]")
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                },
                Width = 150,
            };
        }

        if (!readOnly && f.IsEditable)
        {
            return new DataGridTextColumn
            {
                Header = f.DisplayName,
                Binding = new Binding($"[{f.Key}]")
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                },
                Width = 120,
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

    private static DataGridTextColumn ReadOnlyColumn(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new Binding(path),
        IsReadOnly = true,
        Width = width,
    };

    /// <summary>操作列：非异常显示「挂起异常」，异常显示「撤销异常」（可见性由行状态驱动）。</summary>
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
            "</StackPanel></DataTemplate>";

        return new DataGridTemplateColumn
        {
            Header = "操作",
            CellTemplate = (DataTemplate)XamlReader.Parse(xaml),
            Width = 130,
        };
    }
}
