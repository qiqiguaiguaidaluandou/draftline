using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Draftline.App.ViewModels;
using Draftline.Core.Enums;
using Draftline.Core.Models;

namespace Draftline.App.Views;

public partial class ExceptionResolveView : UserControl
{
    private bool _formBuilt;

    public ExceptionResolveView() => InitializeComponent();

    // 按字段 schema 竖排建表单（视图职责）：待填列给可编辑控件、其余只读文本。
    // 单元格绑定走 RowViewModel 的字符串索引器 [key]，与批次作业网格一致；FormHost.DataContext = Row。
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ExceptionResolveViewModel vm) return;
        if (_formBuilt) return;

        FormHost.Children.Clear();
        foreach (var f in vm.Fields.OrderBy(x => x.Order))
            FormHost.Children.Add(BuildFieldRow(f));

        _formBuilt = true;
    }

    /// <summary>一行 = 标签（左，含必填标记）+ 编辑器（右）。</summary>
    private static FrameworkElement BuildFieldRow(FieldDefinition f)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = f.IsEditable && f.IsRequired ? f.DisplayName + " *" : f.DisplayName,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextSecondary"],
            Margin = new Thickness(0, 0, 12, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var editor = BuildEditor(f);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);

        return grid;
    }

    private static FrameworkElement BuildEditor(FieldDefinition f)
    {
        // 待填下拉（如"是否机加中心可以做"）：常驻 ComboBox。
        if (f.IsEditable && f.Editor == FieldEditor.Dropdown)
        {
            var combo = new ComboBox
            {
                ItemsSource = f.Options,
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 200,
                VerticalAlignment = VerticalAlignment.Center,
            };
            combo.SetBinding(ComboBox.SelectedItemProperty, new Binding($"[{f.Key}]")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
            return combo;
        }

        // 待填文本（如目标价）：常驻 TextBox。
        if (f.IsEditable)
        {
            var box = new TextBox { VerticalAlignment = VerticalAlignment.Center };
            box.SetBinding(TextBox.TextProperty, new Binding($"[{f.Key}]")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
            return box;
        }

        // 只读字段：纯文本展示。
        var text = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        text.SetBinding(TextBlock.TextProperty, new Binding($"[{f.Key}]"));
        return text;
    }
}
