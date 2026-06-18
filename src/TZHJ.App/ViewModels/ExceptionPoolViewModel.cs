using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TZHJ.App.Services;
using TZHJ.Core.Contracts;
using TZHJ.Core.Enums;
using TZHJ.Core.Models;

namespace TZHJ.App.ViewModels;

/// <summary>
/// 异常待跟进池（跨批次、本流程内）。记来源批次，后续解决后可补处理（骨架先只读列示）。
/// </summary>
public sealed partial class ExceptionPoolViewModel : ViewModelBase
{
    private readonly ILocalBatchStore _store;
    private readonly ISession _session;
    private readonly IExplorerService _explorer;
    private readonly INavigationService _nav;
    private readonly FlowType _flow;

    public ExceptionPoolViewModel(
        ILocalBatchStore store, ISession session, IExplorerService explorer, INavigationService nav, FlowType flow)
    {
        _store = store;
        _session = session;
        _explorer = explorer;
        _nav = nav;
        _flow = flow;

        Title = $"{(flow == FlowType.Pricing ? "图纸核价" : "挑图纸")} · 异常待跟进";
        PoolPath = LocalPaths.LocalExceptionPoolRoot(session.Config.LocalRoot, flow);
    }

    public string PoolPath { get; }

    [ObservableProperty] private bool _showGroupColumn;

    public ObservableCollection<ExceptionItem> Items { get; } = new();

    public override async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Items.Clear();
            var list = await _store.ListExceptionsAsync(_flow, _session.Operator.EmployeeId);
            
            // 权限感应：如果列表中存在超过一个不同的组名，则显示组列
            ShowGroupColumn = list.Select(e => e.GroupName).Distinct().Count() > 1;

            foreach (var item in list)
                Items.Add(item);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenFolder() => _explorer.OpenFolder(PoolPath);

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    /// <summary>补处理：进入该行的全字段补处理页（重填 + 单行补回传）。</summary>
    [RelayCommand]
    private void Resolve(ExceptionItem item) => _nav.ToExceptionResolve(_flow, item);
}
