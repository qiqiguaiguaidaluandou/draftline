using System.Windows;
using System.Windows.Media;

namespace TZHJ.App.Behaviors;

/// <summary>
/// 把任意 FrameworkElement 裁剪成圆角矩形（随尺寸自适应）。
/// 用于给 DataGrid 这类自身不支持圆角的控件做圆角边界。用法：b:CornerClip.Radius="10"。
/// </summary>
public static class CornerClip
{
    public static readonly DependencyProperty RadiusProperty =
        DependencyProperty.RegisterAttached(
            "Radius", typeof(double), typeof(CornerClip),
            new PropertyMetadata(0d, OnRadiusChanged));

    public static double GetRadius(DependencyObject o) => (double)o.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject o, double value) => o.SetValue(RadiusProperty, value);

    private static void OnRadiusChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not FrameworkElement fe) return;
        fe.SizeChanged -= OnSizeChanged;
        fe.SizeChanged += OnSizeChanged;
        ApplyClip(fe);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => ApplyClip((FrameworkElement)sender);

    private static void ApplyClip(FrameworkElement fe)
    {
        if (fe.ActualWidth <= 0 || fe.ActualHeight <= 0) return;
        var r = GetRadius(fe);
        fe.Clip = new RectangleGeometry(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight), r, r);
    }
}
