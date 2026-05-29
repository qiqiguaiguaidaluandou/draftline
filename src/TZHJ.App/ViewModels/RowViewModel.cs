using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using TZHJ.Core.Enums;
using TZHJ.Core.Models;

namespace TZHJ.App.ViewModels;

/// <summary>
/// 网格中的一行。单元格绑定走字符串索引器 <c>this[key]</c>（支持配置化字段）。
/// 待填列填写后自动判为「已处理」、清空回「待处理」（无需点确认）；挂起 / 撤销异常由父 VM 命令驱动。
/// </summary>
public sealed class RowViewModel : ObservableObject
{
    private readonly MaterialRow _row;
    private readonly IReadOnlyCollection<string> _requiredKeys;
    private readonly IReadOnlyCollection<string> _editableKeys;
    private readonly Action _onChanged;
    private readonly bool _readOnly;

    public RowViewModel(
        MaterialRow row,
        IReadOnlyCollection<string> requiredKeys,
        IReadOnlyCollection<string> editableKeys,
        Action onChanged,
        bool readOnly)
    {
        _row = row;
        _requiredKeys = requiredKeys;
        _editableKeys = editableKeys;
        _onChanged = onChanged;
        _readOnly = readOnly;
    }

    public MaterialRow Model => _row;
    public string RowKey => _row.RowKey;

    /// <summary>单元格绑定入口（key = 字段键）。改动待填列即自动判定行状态。</summary>
    public string? this[string key]
    {
        get => _row.Get(key);
        set
        {
            if (_row.Get(key) == value) return;
            _row.Set(key, value);
            if (!_readOnly && _editableKeys.Contains(key))
            {
                Reevaluate();
                _onChanged();
            }
        }
    }

    /// <summary>按"待填列是否填齐"自动在 待处理 / 已处理 间切换（异常、已上传不自动翻转）。</summary>
    private void Reevaluate()
    {
        if (_row.Status is RowStatus.Exception or RowStatus.Uploaded) return;
        var allFilled = _requiredKeys.Count > 0
                        && _requiredKeys.All(k => !string.IsNullOrWhiteSpace(_row.Get(k)));
        var target = allFilled ? RowStatus.Done : RowStatus.Pending;
        if (_row.Status != target)
        {
            _row.Status = target;
            NotifyStateChanged();
        }
    }

    public void Suspend(string reason)
    {
        _row.Status = RowStatus.Exception;
        _row.ExceptionReason = reason;
        NotifyStateChanged();
    }

    public void Restore()
    {
        _row.ExceptionReason = null;
        _row.Status = RowStatus.Pending;
        Reevaluate(); // 若待填列已填，直接回到已处理
        NotifyStateChanged();
    }

    /// <summary>整批回传成功后，正常行置为已上传。</summary>
    public void MarkUploaded()
    {
        _row.Status = RowStatus.Uploaded;
        NotifyStateChanged();
    }

    public RowStatus Status => _row.Status;
    public bool IsException => _row.Status == RowStatus.Exception;

    public string StatusText => _row.Status switch
    {
        RowStatus.Pending => "待处理",
        RowStatus.Done => "已处理",
        RowStatus.Exception => "挂起异常",
        RowStatus.Uploaded => "已上传",
        _ => "",
    };

    public string StatusKind => _row.Status switch
    {
        RowStatus.Pending => "Gray",
        RowStatus.Done => "Green",
        RowStatus.Exception => "Orange",
        RowStatus.Uploaded => "Green",
        _ => "Gray",
    };

    public string ExceptionReason => _row.ExceptionReason ?? string.Empty;

    public string DrawingText
    {
        get
        {
            if (_row.Drawings.Count == 0) return "⚠ 无图纸";
            if (_row.Drawings.Any(d => !d.Exists)) return "⚠ 缺失";
            return string.Join("·", _row.Drawings.Select(d => d.Kind).Distinct());
        }
    }

    // 操作列按钮可见性：非异常显示「挂起异常」，异常显示「撤销异常」。
    public Visibility SuspendVisibility =>
        _row.Status is RowStatus.Pending or RowStatus.Done ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RestoreVisibility =>
        _row.Status == RowStatus.Exception ? Visibility.Visible : Visibility.Collapsed;

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsException));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusKind));
        OnPropertyChanged(nameof(ExceptionReason));
        OnPropertyChanged(nameof(SuspendVisibility));
        OnPropertyChanged(nameof(RestoreVisibility));
    }
}
