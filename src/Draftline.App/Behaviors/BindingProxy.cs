using System.Windows;

namespace Draftline.App.Behaviors;

/// <summary>
/// 绑定代理：DataGridColumn 不在可视化树中、也不继承 DataContext，列自身属性（如 Visibility）
/// 上的 RelativeSource/ElementName 绑定会静默失效。把本代理放进资源、Data 绑到 UserControl 的
/// DataContext，列即可通过 Source={StaticResource ...} 间接访问 ViewModel。
/// 用法见 BatchListView.xaml 的"产品线组"列。
/// </summary>
public sealed class BindingProxy : Freezable
{
    protected override Freezable CreateInstanceCore() => new BindingProxy();

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));
}
