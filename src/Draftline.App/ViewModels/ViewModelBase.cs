using CommunityToolkit.Mvvm.ComponentModel;

namespace Draftline.App.ViewModels;

/// <summary>所有页面 ViewModel 的基类。</summary>
public abstract partial class ViewModelBase : ObservableObject, IDisposable
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>进入页面时加载数据（由 View 的 Loaded 调用）。</summary>
    public virtual Task LoadAsync() => Task.CompletedTask;

    /// <summary>导航离开本页时由 NavigationService 调用，释放页面持有的资源（如 FileSystemWatcher）。默认无操作。</summary>
    public virtual void Dispose() { }
}
