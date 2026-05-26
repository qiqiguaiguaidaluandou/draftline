using System.Collections.ObjectModel;
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
    private readonly FlowType _flow;

    public ExceptionPoolViewModel(ILocalBatchStore store, ISession session, IExplorerService explorer, FlowType flow)
    {
        _store = store;
        _session = session;
        _explorer = explorer;
        _flow = flow;

        Title = $"{(flow == FlowType.Pricing ? "图纸核价" : "挑图纸")} · 异常待跟进";
        PoolPath = LocalPaths.ExceptionPoolRoot(session.Config.LocalRoot, flow, session.Operator.EmployeeId);
    }

    public string PoolPath { get; }
    public ObservableCollection<ExceptionItem> Items { get; } = new();

    public override async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Items.Clear();
            foreach (var item in await _store.ListExceptionsAsync(_flow, _session.Operator.EmployeeId))
                Items.Add(item);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenFolder() => _explorer.OpenFolder(PoolPath);

    [RelayCommand]
    private Task Refresh() => LoadAsync();
}
