using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Draftline.App.Services;
using Draftline.Core.Contracts;
using Draftline.Core.Contracts.Http;
using Draftline.Core.Enums;
using Draftline.Core.Models;
using Draftline.Core.Schemas;
using Draftline.Core.Validation;

namespace Draftline.App.ViewModels;

/// <summary>
/// 批次作业页：渲染可编辑网格（列由字段 schema 驱动）、提交闸门、整批上传（回传成功后移入已处理）。
/// 已处理批次只读查看。挂起异常行整批提交时不回传，转入异常池。
/// </summary>
public sealed partial class BatchWorkViewModel : ViewModelBase
{
    private readonly ILocalBatchStore _store;
    private readonly IDataGateway _data;
    private readonly ISubmitGateway _submit;
    private readonly IFieldProvider _fieldProvider;
    private readonly ISession _session;
    private readonly INavigationService _nav;
    private readonly IDialogService _dialog;
    private readonly IExplorerService _explorer;

    private readonly FlowType _flow;
    private readonly BatchLocation _location;
    private readonly string _groupName;
    private readonly string _folderName;
    private Batch? _batch;

    public BatchWorkViewModel(
        ILocalBatchStore store, IDataGateway data, ISubmitGateway submit, IFieldProvider fieldProvider,
        ISession session, INavigationService nav, IDialogService dialog, IExplorerService explorer,
        FlowType flow, BatchLocation location, string groupName, string folderName)
    {
        _store = store;
        _data = data;
        _submit = submit;
        _fieldProvider = fieldProvider;
        _session = session;
        _nav = nav;
        _dialog = dialog;
        _explorer = explorer;
        _flow = flow;
        _location = location;
        _groupName = groupName;
        _folderName = folderName;

        Fields = _fieldProvider.FieldsFor(flow);
        IsReadOnly = location == BatchLocation.Done;
        TargetSystem = flow == FlowType.Pricing ? "SRM" : "EBS";

        // 可筛选列（Excel 风表头 ▼）：核价=物料编码/型号/名称/变更状态；挑图=产品线/申请人名称/项目。
        FilterableKeys = flow == FlowType.Pricing
            ? new HashSet<string>
            {
                FieldSchemas.PricingKeys.MaterialCode, FieldSchemas.PricingKeys.Model,
                FieldSchemas.PricingKeys.Name, FieldSchemas.PricingKeys.HasChange,
            }
            : new HashSet<string>
            {
                FieldSchemas.DrawingKeys.ProductLine, FieldSchemas.DrawingKeys.Applicant,
                FieldSchemas.DrawingKeys.Project,
            };
    }

    public IReadOnlyList<FieldDefinition> Fields { get; }

    /// <summary>启用表头筛选（▼）的列键集合；仅影响显示，统计与提交闸门仍按全部行计算。</summary>
    public IReadOnlySet<string> FilterableKeys { get; }
    public ObservableCollection<RowViewModel> Rows { get; } = new();

    public bool IsReadOnly { get; }
    public string TargetSystem { get; }

    /// <summary>整批回传按钮文案：核价→回传到SRM，挑图→回传到EBS。</summary>
    public string SubmitButtonText => $"回传到{TargetSystem}";

    [ObservableProperty] private string _folderPathText = string.Empty;

    /// <summary>自动暂存提示（如"已自动暂存 · 14:03:21"），信息条上轻量展示，不弹 Toast。</summary>
    [ObservableProperty] private string _autoSaveHint = string.Empty;

    public int PendingCount => Rows.Count(r => r.Status == RowStatus.Pending);
    public int DoneCount => Rows.Count(r => r.Status is RowStatus.Done or RowStatus.Uploaded);
    public int ExceptionCount => Rows.Count(r => r.Status == RowStatus.Exception);
    public string ProgressText => $"已处理 {DoneCount + ExceptionCount} / {Rows.Count}";

    public bool CanSubmit => !IsReadOnly && Rows.Count > 0 && PendingCount == 0 && _session.Operator.CanSubmit;

    public string GateText => CanSubmit
        ? $"✅ 全部行已处理完毕（已填写或挂起异常），可整批回传 {TargetSystem}。"
        : $"还有 {PendingCount} 行处于「待处理」，每行需先填写或挂起异常，「上传」才可点（提交闸门）。";

    public override async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            _batch = await _store.GetBatchAsync(_flow, _session.Operator.EmployeeId, _location, _groupName, _folderName);
            if (_batch is null)
            {
                _dialog.Error("批次不存在或已被删除。");
                _nav.ToBatchList(_flow, _location);
                return;
            }

            Title = $"批次作业 · {_batch.GroupName} · {_batch.WindowStart:MM-dd HH:mm} ~ {_batch.WindowEnd:MM-dd HH:mm}";
            FolderPathText = _batch.FolderPath;

            var requiredKeys = Fields.Where(f => f.IsEditable && f.IsRequired).Select(f => f.Key).ToHashSet();
            var editableKeys = Fields.Where(f => f.IsEditable).Select(f => f.Key).ToHashSet();

            Rows.Clear();
            foreach (var row in _batch.Rows)
                Rows.Add(new RowViewModel(row, requiredKeys, editableKeys, OnRowChanged, IsReadOnly));

            Recompute();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (_batch is not null) _explorer.OpenFolder(_batch.FolderPath);
    }

    [RelayCommand]
    private async Task Back()
    {
        await FlushAutoSaveAsync(); // 离开前把未落盘的改动写回
        _nav.ToBatchList(_flow, _location);
    }

    [RelayCommand]
    private async Task MarkException(RowViewModel row)
    {
        // 不预填默认原因，由操作员自己写（沿用"原因为空则不挂起"）。
        var reason = _dialog.Prompt("挂起异常", "请填写异常原因：",
            row.ExceptionReason is { Length: > 0 } ? row.ExceptionReason : null);
        if (string.IsNullOrWhiteSpace(reason)) return;

        IsBusy = true;
        try
        {
            // --- Remote-First: 同步到云端异常池 ---
            await _data.SuspendExceptionAsync(new SuspendExceptionRequest
            {
                Flow = _flow,
                GroupName = _batch?.GroupName ?? "Default",
                BatchId = _batch?.FolderName ?? "",
                RowKey = row.RowKey,
                MaterialCode = row.Model.Get("materialCode") ?? row.RowKey,
                DisplayName = row.Model.Get("name") ?? row.Model.Get("materialDesc"),
                Reason = reason
            });

            row.Suspend(reason);
            Recompute();
            await SaveCoreAsync(); // 更新本地镜像
        }
        catch (Exception ex)
        {
            _dialog.Error(FriendlyError.Describe(ex, "挂起异常"));
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task Restore(RowViewModel row)
    {
        IsBusy = true;
        try
        {
            // --- Remote-First: 从云端异常池移除 ---
            await _data.ResolveExceptionAsync(_flow, _batch?.GroupName ?? "Default", _batch?.FolderName ?? "", row.RowKey);

            row.Restore();
            Recompute();
            await SaveCoreAsync(); // 更新本地镜像
        }
        catch (Exception ex)
        {
            _dialog.Error(FriendlyError.Describe(ex, "撤销异常"));
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 重新获取某一料号的图纸：让服务端再向 PLM 拉一次该料号图纸，落到服务器批次目录后同步到本地。
    /// 落盘为覆盖语义——同名图纸覆盖、无则新增。取数时缺图 / 图纸有更新时手动补齐用。
    /// </summary>
    [RelayCommand]
    private async Task RefetchDrawing(RowViewModel row)
    {
        if (_batch is null || IsReadOnly || row is null) return;

        var code = row.Model.Get("materialCode")?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            _dialog.Error("该行缺少物料编码，无法获取图纸。");
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _data.RefetchDrawingAsync(new RefetchDrawingRequest
            {
                Flow = _flow,
                GroupName = _batch.GroupName,
                BatchId = _batch.Key,
                RowKey = row.RowKey,
                MaterialCode = code,
            });

            if (!result.Found || result.Files.Count == 0)
            {
                _dialog.Error($"重新获取图纸失败：PLM 中暂无料号「{code}」的图纸，请稍后再试。");
                return;
            }

            // 服务端已按覆盖语义落好（同名覆盖 / 无则新增）；逐个下载到本地批次目录并并入该行图纸清单。
            foreach (var fileName in result.Files)
            {
                var bytes = await _data.DownloadFileAsync(_flow, _batch.GroupName, _batch.Key, fileName);
                await _store.WriteSyncFileAsync(_flow, _batch.GroupName, _batch.Key, _location, fileName, bytes);

                // 同名图纸文件已覆盖、引用无需重复添加；仅新图纸补进内存行的图纸引用（去重）。
                if (!row.Model.Drawings.Any(d => string.Equals(d.FileName, fileName, System.StringComparison.OrdinalIgnoreCase)))
                {
                    row.Model.Drawings.Add(new DrawingRef
                    {
                        FileName = fileName,
                        MaterialCode = code,
                        Kind = System.IO.Path.GetExtension(fileName).TrimStart('.').ToUpperInvariant(),
                        Exists = true,
                    });
                }
            }

            // 持久化"行↔图纸"关联到本地 manifest（不动行状态/填写进度）。
            await _store.AddRowDrawingsAsync(_flow, _batch.GroupName, _batch.Key, _location, row.RowKey, result.Files);

            _dialog.Success($"图纸获取成功：料号「{code}」已拉取 {result.Files.Count} 张图纸（同名覆盖、新增追加）。" +
                            "可点「打开文件夹（含图纸）」查看。");
        }
        catch (Exception ex)
        {
            _dialog.Error(FriendlyError.Describe(ex, "重新获取图纸"));
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task Submit()
    {
        if (_batch is null) return;

        // 提交闸门二次校验（防御 UI 状态与模型短暂不一致）：仍有「待处理」行则中止。
        if (Rows.Count == 0 || Rows.Any(r => r.Status == RowStatus.Pending))
        {
            _dialog.Error("仍有「待处理」行，请先填写或挂起异常后再整批提交。");
            Recompute();
            return;
        }

        var normal = Rows.Where(r => r.Status != RowStatus.Exception).ToList();
        var exceptions = Rows.Where(r => r.Status == RowStatus.Exception).ToList();

        // 回传前把关待填列取值（如核价目标价：须为有效数字、大于 0、最多两位小数）。
        // 只校验将要回传的正常行；挂起异常行不回传，不拦。非法则中止、逐行提示。
        var invalid = new List<string>();
        foreach (var r in normal)
        {
            foreach (var f in Fields)
            {
                var err = FieldRules.Validate(f, r.Model.Get(f.Key));
                if (err is null) continue;
                var code = r.Model.Get("materialCode") ?? r.RowKey;
                invalid.Add($"· {code}：{err}");
            }
        }
        if (invalid.Count > 0)
        {
            _dialog.Error("以下行填写有误，请修正后再回传：\n" + string.Join("\n", invalid));
            return;
        }

        if (!_dialog.Confirm("确认整批上传",
                $"正常行（回传 {TargetSystem}）：{normal.Count} 行\n" +
                $"挂起异常行（不回传，转入异常池）：{exceptions.Count} 行\n\n" +
                $"回传由后端执行并记审计日志，成功后批次将移入「已处理」。确认？"))
            return;

        IsBusy = true;
        try
        {
            var request = new SubmitRequest
            {
                EmployeeId = _session.Operator.EmployeeId,
                Flow = _flow,
                GroupName = _batch.GroupName,
                BatchKey = _batch.Key,
                WindowStart = _batch.WindowStart,
                WindowEnd = _batch.WindowEnd,
                Rows = normal.Select(r => new SubmitRow
                {
                    RowKey = r.RowKey,
                    Values = new Dictionary<string, string?>(r.Model.Values),
                }).ToList(),
            };

            var result = await _submit.SubmitBatchAsync(request);
            if (!result.Success)
            {
                _dialog.Error(result.Message ?? "回传失败，请重试。");
                return;
            }

            // 逐行结果：成功行→已上传；被目标系统永久退回的行（如“物料不存在”）→挂异常池，
            // 与手动挂起的异常同等对待，可在“异常解决”页补回传（待主数据修正后重提）。
            var failedReasons = result.RowResults
                .Where(r => !r.Success)
                .ToDictionary(r => r.RowKey, r => r.Message);

            foreach (var r in normal)
            {
                if (failedReasons.TryGetValue(r.RowKey, out var remark))
                    r.Suspend($"{TargetSystem}回传失败：{remark ?? "未知原因"}");
                else
                    r.MarkUploaded();
            }

            // 手动挂起 + 目标系统退回，统一入异常池（按当前状态重新取，含上面新挂的）。
            var exceptionRows = Rows.Where(r => r.Status == RowStatus.Exception).ToList();
            if (exceptionRows.Count > 0)
            {
                var items = exceptionRows.Select(r => new ExceptionItem
                {
                    Flow = _flow,
                    GroupName = _batch.GroupName, // 必填：漏设会落到模型默认 "Default"，令异常池产品线组显示错误
                    RowKey = r.RowKey,
                    MaterialCode = r.Model.Get("materialCode") ?? r.RowKey,
                    DisplayName = r.Model.Get("name") ?? r.Model.Get("materialDesc"),
                    SourceBatch = _batch.FolderName,
                    Reason = r.Model.ExceptionReason ?? "未知",
                    SuspendedAt = DateTime.Now,
                });
                await _store.AddExceptionsAsync(_flow, _session.Operator.EmployeeId, items);
            }

            await _store.SaveBatchAsync(_batch);
            await _store.MoveToDoneAsync(_batch);

            var uploadedCount = normal.Count - failedReasons.Count;
            var rejectedCount = failedReasons.Count;
            var msg = $"上传完成：{uploadedCount} 行已回传 {TargetSystem}";
            if (rejectedCount > 0) msg += $"，{rejectedCount} 行被退回并转入异常池";
            if (exceptions.Count > 0) msg += $"，{exceptions.Count} 行手动挂起";
            msg += "。批次已移入「已处理」。";
            _dialog.Success(msg);
            _nav.ToBatchList(_flow, BatchLocation.Todo);
        }
        catch (Exception ex)
        {
            // 断网/超时/服务器报错翻成操作员可读提示；批次未移入「已处理」，可原样重试。
            _dialog.Error(FriendlyError.Describe(ex, "回传"));
        }
        finally { IsBusy = false; }
    }

    private void Recompute()
    {
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(DoneCount));
        OnPropertyChanged(nameof(ExceptionCount));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(GateText));
        SubmitCommand.NotifyCanExecuteChanged();
    }

    // ---------- 自动暂存（填写/挂起后无需手点「暂存」）----------

    private CancellationTokenSource? _autoSaveDebounce;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _dirty;

    /// <summary>行值改动（编辑待填列）：重算闸门 + 防抖自动暂存。</summary>
    private void OnRowChanged(RowViewModel row)
    {
        Recompute();
        ScheduleAutoSave();
    }

    /// <summary>标脏并防抖：连续输入合并到停手 ~800ms 后才落盘一次，避免每个键都写 xlsx。</summary>
    private void ScheduleAutoSave()
    {
        if (_batch is null || IsReadOnly) return;
        _dirty = true;
        _autoSaveDebounce?.Cancel();
        _autoSaveDebounce = new CancellationTokenSource();
        var token = _autoSaveDebounce.Token;
        _ = Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            try { await Task.Delay(800, token); }
            catch (TaskCanceledException) { return; }
            if (token.IsCancellationRequested) return;
            await AutoSaveAsync();
        });
    }

    private async Task AutoSaveAsync()
    {
        if (!_dirty) return;
        try
        {
            await SaveCoreAsync();
            AutoSaveHint = $"已同步服务器 · {DateTime.Now:HH:mm:ss}";
        }
        catch
        {
            // 自动暂存失败不打断填写，只提示；操作员仍可点「暂存」或离开时再尝试。
            AutoSaveHint = "云端同步失败，请检查网络";
        }
    }

    /// <summary>离开/关闭前把未落盘改动写回。</summary>
    private async Task FlushAutoSaveAsync()
    {
        _autoSaveDebounce?.Cancel();
        if (_dirty) await AutoSaveAsync();
    }

    /// <summary>串行化落盘：自动暂存、手动暂存、离开时刷盘共用，避免并发写同一 xlsx。</summary>
    private async Task SaveCoreAsync()
    {
        if (_batch is null || IsReadOnly) return;
        await _saveLock.WaitAsync();
        try
        {
            // --- Remote-First: 找出所有脏行并同步到服务器 ---
            var dirtyRows = Rows.Where(r => r.IsDirty).ToList();
            foreach (var row in dirtyRows)
            {
                await _data.UpdateRowAsync(new UpdateRowRequest
                {
                    Flow = _flow,
                    GroupName = _batch.GroupName,
                    BatchId = _batch.FolderName,
                    RowKey = row.RowKey,
                    Values = new Dictionary<string, string?>(row.Model.Values)
                });
                row.IsDirty = false;
            }

            // 同步成功后，再更新本地镜像
            await _store.SaveBatchAsync(_batch);
            _dirty = false;
        }
        finally { _saveLock.Release(); }
    }

    public override void Dispose()
    {
        _autoSaveDebounce?.Cancel();
        // 导航/关闭时尽力写回未落盘改动（本地写通常即时完成；经 _saveLock 串行不与进行中的保存打架）。
        if (_dirty && _batch is not null && !IsReadOnly) _ = SaveCoreAsync();
    }
}
