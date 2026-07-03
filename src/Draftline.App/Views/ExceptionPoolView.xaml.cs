using System.Windows.Controls;

namespace Draftline.App.Views;

public partial class ExceptionPoolView : UserControl
{
    // 数据加载由 NavigationService 在导航时触发（见 INavigationService.Raise）。
    public ExceptionPoolView() => InitializeComponent();
}
