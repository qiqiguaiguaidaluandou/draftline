using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Draftline.App.Services;
using Draftline.Core.Contracts;
using Draftline.Core.Enums;
using Draftline.Core.Models;
using Draftline.Infrastructure.Options;
using Draftline.Infrastructure.Sync;

namespace Draftline.App.ViewModels;

/// <summary>
/// 批次列表（待处理 / 已处理）。直接映射本地文件夹：扫描目录 → 列表。
/// 待处理可"手动补拉"（演示取数→落本地）与"开始作业"。
/// </summary>
public sealed partial class BatchListViewModel : ViewModelBase
{
    private readonly ILocalBatchStore _store;
    private readonly BatchSyncService _sync;
    private readonly ISession _session;
    private readonly INavigationService _nav;
    private readonly IDialogService _dialog;
    private readonly IExplorerService _explorer;

    private readonly FlowType _flow;
    private readonly BatchLocation _location;

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _refreshDebounce;

    public BatchListViewModel(
        ILocalBatchStore store, BatchSyncService sync, ISession session,
        INavigationService nav, IDialogService dialog, IExplorerService explorer,
        LocalStorageOptions storage, FlowType flow, BatchLocation location)
    {
        _store = store;
        _sync = sync;
        _session = session;
        _nav = nav;
        _dialog = dialog;
        _explorer = explorer;
        _flow = flow;
        _location = location;

        var flowName = flow == FlowType.Pricing ? "图纸核价" : "挑图纸";
        var locName = location == BatchLocation.Todo ? "待处理" : "已处理";
        Title = $"{flowName} · {locName}";
        IsTodo = location == BatchLocation.Todo;
        LocationRootPath = LocalPaths.LocalLocationRoot(storage.Root, flow, location);
        StartWatching();
    }

    public bool IsTodo { get; }
    public string LocationRootPath { get; }

    [ObservableProperty] private bool _showGroupColumn;

    public ObservableCollection<BatchRowVM> Batches { get; } = new();

    public override async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Batches.Clear();
            var list = await _store.ListBatchesAsync(_flow, _session.Operator.EmployeeId, _location);
            
            // 核价恒显示产品线组列：即便当前只有一个组，也需标明是组1还是组2，否则无法区分数据归属。
            // 挑图统一用 Center、不分组 → 永不显示该列。
            ShowGroupColumn = _flow == FlowType.Pricing;

            foreach (var b in list)
                Batches.Add(new BatchRowVM(b));
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    [RelayCommand]
    private void OpenLocationFolder() => _explorer.OpenFolder(LocationRootPath);

    [RelayCommand]
    private void OpenBatchFolder(BatchRowVM row) => _explorer.OpenFolder(row.Batch.FolderPath);

    [RelayCommand]
    private void OpenBatch(BatchRowVM row) =>
        _nav.ToBatchWork(_flow, _location, row.Batch.FolderName);

    /// <summary>手动同步：触发一次镜像同步，补齐服务器上已存在但本地尚未镜像的批次。</summary>
    [RelayCommand]
    private async Task RefreshMirror()
    {
        IsBusy = true;
        try
        {
            // --- New Architecture: Pure Mirror Refresh ---
            await _sync.MirrorSyncAsync(_session.Operator.EmployeeId);
            await LoadAsync();
            _dialog.Info("同步数据完成。");
        }
        catch (Exception ex)
        {
            _dialog.Error(FriendlyError.Describe(ex, "同步失败"));
        }
        finally { IsBusy = false; }
    }

    // ---------- 文件夹即真相源：FileSystemWatcher 同步 ----------

    /// <summary>监视本位置目录的子目录增删/改名，外部变化去抖后自动刷新列表。
    /// 删除走甲案：如实同步（该批次从列表消失）、不阻止、不做持久作废标记。</summary>
    private void StartWatching()
    {
        try
        {
            Directory.CreateDirectory(LocationRootPath); // 目录可能尚未建（无批次时），监视前确保存在
            _watcher = new FileSystemWatcher(LocationRootPath)
            {
                NotifyFilter = NotifyFilters.DirectoryName,
                IncludeSubdirectories = true,
            };
            _watcher.Created += OnFolderChanged;
            _watcher.Deleted += OnFolderChanged;
            _watcher.Renamed += OnFolderChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            _watcher = null; // 监视起不来不影响列表本身（仍可手动刷新）
        }
    }

    private void OnFolderChanged(object sender, FileSystemEventArgs e) => ScheduleRefresh();

    /// <summary>去抖：文件操作常成串到达，合并到最后一次后再在 UI 线程刷新。</summary>
    private void ScheduleRefresh()
    {
        _refreshDebounce?.Cancel();
        _refreshDebounce = new CancellationTokenSource();
        var token = _refreshDebounce.Token;
        _ = Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            try { await Task.Delay(300, token); }
            catch (TaskCanceledException) { return; }
            if (token.IsCancellationRequested) return;
            try { await LoadAsync(); }
            catch { /* 刷新失败不崩溃 */ }
        });
    }

    public override void Dispose()
    {
        _refreshDebounce?.Cancel();
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFolderChanged;
            _watcher.Deleted -= OnFolderChanged;
            _watcher.Renamed -= OnFolderChanged;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}

/// <summary>批次列表的一行（只读投影）。</summary>
public sealed class BatchRowVM
{
    public BatchRowVM(Batch batch) => Batch = batch;

    public Batch Batch { get; }

    public string GroupName => Batch.GroupName;
    public string WindowText => $"{Batch.WindowStart:MM-dd HH:mm} ~ {Batch.WindowEnd:MM-dd HH:mm}";
    public string FolderName => Batch.FolderName;
    public int MaterialCount => Batch.MaterialCount;

    public string ProgressText => Batch.Location == BatchLocation.Todo
        ? $"{Batch.DoneCount + Batch.ExceptionCount} / {Batch.MaterialCount}"
        : $"正常 {Batch.DoneCount} / 异常 {Batch.ExceptionCount}";

    public string StatusText => Batch.Location switch
    {
        BatchLocation.Done => "已处理",
        _ when Batch.DoneCount == 0 && Batch.ExceptionCount == 0 => "未处理",
        _ => "处理中",
    };

    // 状态色键（与 StatusKindToBrush/StatusKindToBg 对齐）：绿=已处理 · 灰=未处理 · 蓝=处理中
    // 异常数已在「进度/统计」列单列显示，批次状态不再因含异常而变橙（避免与行级异常的橙撞色、文字仍为"处理中"造成误读）。
    public string StatusKind => Batch.Location switch
    {
        BatchLocation.Done => "Green",
        _ when Batch.DoneCount == 0 && Batch.ExceptionCount == 0 => "Gray",
        _ => "Blue",
    };

    public string FetchedText => Batch.FetchedAt.ToString("MM-dd HH:mm");
    public string SubmittedText => Batch.SubmittedAt?.ToString("MM-dd HH:mm") ?? "—";
    public bool IsTodo => Batch.Location == BatchLocation.Todo;
    public bool IsPricingFlow => Batch.Flow == FlowType.Pricing;
}
