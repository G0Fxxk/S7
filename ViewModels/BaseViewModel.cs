using System.IO;
using S7WpfApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace S7WpfApp.ViewModels;

/// <summary>
/// ViewModel 基类
/// </summary>
public partial class BaseViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// 是否正在加载/处理中
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// 页面标题
    /// </summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// 状态消息
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// 是否有错误
    /// </summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// 错误消息
    /// </summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// 设置错误状态
    /// </summary>
    protected void SetError(string message)
    {
        HasError = true;
        ErrorMessage = message;
        StatusMessage = message;
    }

    /// <summary>
    /// 清除错误状态
    /// </summary>
    protected void ClearError()
    {
        HasError = false;
        ErrorMessage = string.Empty;
    }

    /// <summary>
    /// 设置状态消息
    /// </summary>
    protected void SetStatus(string message)
    {
        StatusMessage = message;
        ClearError();
    }

    // ═══════════════ IDisposable ═══════════════

    private bool _disposed;

    /// <summary>
    /// 释放资源（子类 override 此方法释放 Timer、事件订阅等）
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
