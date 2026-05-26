using CommunityToolkit.Mvvm.ComponentModel;

namespace TZHJ.App.ViewModels;

/// <summary>所有页面 ViewModel 的基类。</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>进入页面时加载数据（由 View 的 Loaded 调用）。</summary>
    public virtual Task LoadAsync() => Task.CompletedTask;
}
