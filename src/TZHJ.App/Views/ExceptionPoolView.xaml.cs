using System.Windows;
using System.Windows.Controls;
using TZHJ.App.ViewModels;

namespace TZHJ.App.Views;

public partial class ExceptionPoolView : UserControl
{
    public ExceptionPoolView() => InitializeComponent();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModelBase vm) await vm.LoadAsync();
    }
}
