using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using S7WpfApp.Models;
using S7WpfApp.Services;

namespace S7WpfApp.ViewModels;

/// <summary>
/// 轴控制页面 ViewModel
/// </summary>
public partial class AxisControlViewModel : ObservableObject, IDisposable
{
    private readonly IPlcService _plcService;
    private readonly IAxisConfigService _axisConfigService;
    private AxisConfig? _config;
    private System.Timers.Timer? _refreshTimer;

    [ObservableProperty]
    private string _axisName = "";

    [ObservableProperty]
    private string _axisGroup = "";

    // ========== 状态显示 ==========

    [ObservableProperty]
    private float _currentPosition;

    [ObservableProperty]
    private bool _negativeLimitActive;

    [ObservableProperty]
    private bool _positiveLimitActive;

    [ObservableProperty]
    private bool _originActive;

    [ObservableProperty]
    private bool _servoEnabled;

    // ========== 输入值 ==========

    [ObservableProperty]
    private float _jogSpeed = 100;

    [ObservableProperty]
    private float _manualSpeed = 100;

    [ObservableProperty]
    private float _manualPosition;

    // ========== 编辑状态标志 ==========

    [ObservableProperty]
    private bool _isEditingJogSpeed;

    [ObservableProperty]
    private bool _isEditingManualSpeed;

    [ObservableProperty]
    private bool _isEditingManualPosition;

    // ========== 点动状态 ==========

    [ObservableProperty]
    private bool _isBackwardPressed;

    [ObservableProperty]
    private bool _isForwardPressed;

    [ObservableProperty]
    private bool _isHomePressed;

    [ObservableProperty]
    private bool _isManualTriggerPressed;

    // ========== 地址属性 (Admin可编辑) ==========

    [ObservableProperty]
    private string _jogSpeedAddress = "";

    [ObservableProperty]
    private string _manualSpeedAddress = "";

    [ObservableProperty]
    private string _manualPositionAddress = "";

    [ObservableProperty]
    private string _currentPositionAddress = "";

    [ObservableProperty]
    private string _negativeLimitAddress = "";

    [ObservableProperty]
    private string _positiveLimitAddress = "";

    [ObservableProperty]
    private string _originAddress = "";

    [ObservableProperty]
    private string _servoEnableAddress = "";

    [ObservableProperty]
    private string _backwardAddress = "";

    [ObservableProperty]
    private string _forwardAddress = "";

    [ObservableProperty]
    private string _homeAddress = "";

    [ObservableProperty]
    private string _manualTriggerAddress = "";

    [ObservableProperty]
    private bool _isAdmin;

    private readonly IAuthService _authService;

    public AxisControlViewModel(IPlcService plcService, IAxisConfigService axisConfigService, IAuthService authService)
    {
        _plcService = plcService;
        _axisConfigService = axisConfigService;
        _authService = authService;
        IsAdmin = _authService.IsAdmin;

        // 订阅趋势采样独占模式（暂停/恢复刷新）
        _plcService.RefreshPauseRequested += (s, paused) =>
        {
            if (paused)
                StopRefresh();
            else if (_config != null && _plcService.IsConnected)
                StartRefresh();
        };
    }

    /// <summary>
    /// 初始化轴配置
    /// </summary>
    public void Initialize(string axisId)
    {
        _config = _axisConfigService.GetById(axisId);
        if (_config == null) return;

        AxisName = _config.Name;
        AxisGroup = _config.Group;

        // 加载地址
        JogSpeedAddress = _config.JogSpeedAddress;
        ManualSpeedAddress = _config.ManualSpeedAddress;
        ManualPositionAddress = _config.ManualPositionAddress;
        CurrentPositionAddress = _config.CurrentPositionAddress;
        NegativeLimitAddress = _config.NegativeLimitAddress;
        PositiveLimitAddress = _config.PositiveLimitAddress;
        OriginAddress = _config.OriginAddress;
        ServoEnableAddress = _config.ServoEnableAddress;
        BackwardAddress = _config.BackwardAddress;
        ForwardAddress = _config.ForwardAddress;
        HomeAddress = _config.HomeAddress;
        ManualTriggerAddress = _config.ManualTriggerAddress;

        StartRefresh();
    }

    /// <summary>
    /// 开始刷新
    /// </summary>
    private void StartRefresh()
    {
        _refreshTimer = new System.Timers.Timer(100);  // 100ms 刷新间隔
        _refreshTimer.Elapsed += async (s, e) => await RefreshAsync();
        _refreshTimer.Start();
    }

    /// <summary>
    /// 停止刷新
    /// </summary>
    public void StopRefresh()
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
    }

    /// <summary>
    /// 刷新状态
    /// </summary>
    private async Task RefreshAsync()
    {
        if (_config == null || !_plcService.IsConnected) return;

        try
        {
            // 读取当前位置 (根据地址格式自动识别类型)
            if (!string.IsNullOrEmpty(CurrentPositionAddress))
            {
                var pos = await _plcService.ReadAutoAsync(CurrentPositionAddress);
                if (pos != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => CurrentPosition = Convert.ToSingle(pos));
                }
            }

            // 读取状态 (根据地址格式自动识别类型)
            if (!string.IsNullOrEmpty(NegativeLimitAddress))
            {
                var val = await _plcService.ReadAutoAsync(NegativeLimitAddress);
                if (val != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => NegativeLimitActive = Convert.ToBoolean(val));
                }
            }

            if (!string.IsNullOrEmpty(PositiveLimitAddress))
            {
                var val = await _plcService.ReadAutoAsync(PositiveLimitAddress);
                if (val != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => PositiveLimitActive = Convert.ToBoolean(val));
                }
            }

            if (!string.IsNullOrEmpty(OriginAddress))
            {
                var val = await _plcService.ReadAutoAsync(OriginAddress);
                if (val != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => OriginActive = Convert.ToBoolean(val));
                }
            }

            if (!string.IsNullOrEmpty(ServoEnableAddress))
            {
                var val = await _plcService.ReadAutoAsync(ServoEnableAddress);
                if (val != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => ServoEnabled = Convert.ToBoolean(val));
                }
            }

            // 读取点动速度（仅在非编辑状态时刷新）
            if (!string.IsNullOrEmpty(JogSpeedAddress) && !IsEditingJogSpeed)
            {
                var val = await _plcService.ReadAutoAsync(JogSpeedAddress);
                if (val != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => JogSpeed = Convert.ToSingle(val));
                }
            }

            // 读取定位速度（仅在非编辑状态时刷新）
            if (!string.IsNullOrEmpty(ManualSpeedAddress) && !IsEditingManualSpeed)
            {
                var val = await _plcService.ReadAutoAsync(ManualSpeedAddress);
                if (val != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => ManualSpeed = Convert.ToSingle(val));
                }
            }

            // 读取目标位置（仅在非编辑状态时刷新）
            if (!string.IsNullOrEmpty(ManualPositionAddress) && !IsEditingManualPosition)
            {
                var val = await _plcService.ReadAutoAsync(ManualPositionAddress);
                if (val != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => ManualPosition = Convert.ToSingle(val));
                }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"轴控制定时读取异常: {ex.Message}"); }
    }

    // ========== 命令 ==========

    /// <summary>
    /// 切换伺服使能
    /// </summary>
    [RelayCommand]
    private async Task ToggleServoAsync()
    {
        if (_config == null || string.IsNullOrEmpty(_config.ServoEnableAddress)) return;

        var newValue = !ServoEnabled;
        await _plcService.WriteAsync(_config.ServoEnableAddress, newValue);
        ServoEnabled = newValue;
    }

    /// <summary>
    /// 写入点动速度
    /// </summary>
    [RelayCommand]
    private async Task WriteJogSpeedAsync()
    {
        if (_config == null || string.IsNullOrEmpty(JogSpeedAddress)) return;
        IsEditingJogSpeed = true;
        await _plcService.WriteAutoAsync(JogSpeedAddress, JogSpeed);
        await Task.Delay(500);
        IsEditingJogSpeed = false;
    }

    /// <summary>
    /// 保存地址配置 (仅管理员)
    /// </summary>
    [RelayCommand]
    private void SaveAddresses()
    {
        if (_config == null || !IsAdmin) return;

        _config.JogSpeedAddress = JogSpeedAddress;
        _config.ManualSpeedAddress = ManualSpeedAddress;
        _config.ManualPositionAddress = ManualPositionAddress;
        _config.CurrentPositionAddress = CurrentPositionAddress;
        _config.NegativeLimitAddress = NegativeLimitAddress;
        _config.PositiveLimitAddress = PositiveLimitAddress;
        _config.OriginAddress = OriginAddress;
        _config.ServoEnableAddress = ServoEnableAddress;
        _config.BackwardAddress = BackwardAddress;
        _config.ForwardAddress = ForwardAddress;
        _config.HomeAddress = HomeAddress;
        _config.ManualTriggerAddress = ManualTriggerAddress;

        _axisConfigService.Save(_config);
    }

    /// <summary>
    /// 写入手动定位速度
    /// </summary>
    [RelayCommand]
    private async Task WriteManualSpeedAsync()
    {
        if (_config == null || string.IsNullOrEmpty(ManualSpeedAddress)) return;
        IsEditingManualSpeed = true;
        await _plcService.WriteAutoAsync(ManualSpeedAddress, ManualSpeed);
        await Task.Delay(500);
        IsEditingManualSpeed = false;
    }

    /// <summary>
    /// 写入手动定位位置
    /// </summary>
    [RelayCommand]
    private async Task WriteManualPositionAsync()
    {
        if (_config == null || string.IsNullOrEmpty(ManualPositionAddress)) return;
        IsEditingManualPosition = true;
        await _plcService.WriteAutoAsync(ManualPositionAddress, ManualPosition);
        await Task.Delay(500);
        IsEditingManualPosition = false;
    }

    // ========== 点动按钮处理 ==========

    /// <summary>
    /// 后退按下
    /// </summary>
    [RelayCommand]
    private async Task BackwardPressAsync()
    {
        if (_config == null || string.IsNullOrEmpty(_config.BackwardAddress)) return;
        IsBackwardPressed = true;
        await _plcService.WriteAsync(_config.BackwardAddress, true);
    }

    /// <summary>
    /// 后退松开
    /// </summary>
    [RelayCommand]
    private async Task BackwardReleaseAsync()
    {
        if (_config == null || string.IsNullOrEmpty(_config.BackwardAddress)) return;
        IsBackwardPressed = false;
        await _plcService.WriteAsync(_config.BackwardAddress, false);
    }

    /// <summary>
    /// 前进按下
    /// </summary>
    [RelayCommand]
    private async Task ForwardPressAsync()
    {
        if (_config == null || string.IsNullOrEmpty(_config.ForwardAddress)) return;
        IsForwardPressed = true;
        await _plcService.WriteAsync(_config.ForwardAddress, true);
    }

    /// <summary>
    /// 前进松开
    /// </summary>
    [RelayCommand]
    private async Task ForwardReleaseAsync()
    {
        if (_config == null || string.IsNullOrEmpty(_config.ForwardAddress)) return;
        IsForwardPressed = false;
        await _plcService.WriteAsync(_config.ForwardAddress, false);
    }

    /// <summary>
    /// 回原位按下
    /// </summary>
    [RelayCommand]
    private async Task HomePressAsync()
    {
        if (_config == null || string.IsNullOrEmpty(_config.HomeAddress)) return;
        IsHomePressed = true;
        await _plcService.WriteAsync(_config.HomeAddress, true);
    }

    /// <summary>
    /// 回原位松开
    /// </summary>
    [RelayCommand]
    private async Task HomeReleaseAsync()
    {
        if (_config == null || string.IsNullOrEmpty(_config.HomeAddress)) return;
        IsHomePressed = false;
        await _plcService.WriteAsync(_config.HomeAddress, false);
    }

    /// <summary>
    /// 手动定位按下
    /// </summary>
    [RelayCommand]
    private async Task ManualTriggerPressAsync()
    {
        if (_config == null || string.IsNullOrEmpty(_config.ManualTriggerAddress)) return;
        IsManualTriggerPressed = true;
        await _plcService.WriteAsync(_config.ManualTriggerAddress, true);
    }

    /// <summary>
    /// 手动定位松开
    /// </summary>
    [RelayCommand]
    private async Task ManualTriggerReleaseAsync()
    {
        if (_config == null || string.IsNullOrEmpty(_config.ManualTriggerAddress)) return;
        IsManualTriggerPressed = false;
        await _plcService.WriteAsync(_config.ManualTriggerAddress, false);
    }

    // ═══════════════ IDisposable ═══════════════

    public void Dispose()
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }
}
