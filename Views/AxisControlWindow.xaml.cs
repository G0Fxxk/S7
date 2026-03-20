using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using S7WpfApp.Models;
using S7WpfApp.Services;

namespace S7WpfApp.Views;

/// <summary>
/// 轴控制窗口 — 实时读取 + 可编辑字段聚焦停读/Enter写入
/// </summary>
public partial class AxisControlWindow : Window
{
    private readonly IPlcService _plcService;
    private readonly AxisConfig _config;
    private readonly DispatcherTimer _timer;
    private bool _isEnabled;

    // 正在编辑的字段名集合（聚焦时停止读取该字段）
    private readonly HashSet<string> _editingFields = new();

    private static readonly SolidColorBrush Green = new(Color.FromRgb(0x4C, 0xAF, 0x50));  // 运行绿
    private static readonly SolidColorBrush Gray = new(Color.FromRgb(0x9E, 0x9E, 0x9E));   // 未激活灰
    private static readonly SolidColorBrush BorderDefault = new(Color.FromRgb(0xD0, 0xD0, 0xD0));

    public AxisControlWindow(AxisConfig config, IPlcService plcService)
    {
        _plcService = plcService;
        _config = config;
        InitializeComponent();

        AxisNameText.Text = config.Name;
        AxisIdText.Text = config.AxisType == AxisType.Rotary ? "旋转轴" : "线性轴";

        // 旋转轴隐藏正负限位
        if (config.AxisType == AxisType.Rotary)
        {
            NegLimitPanel.Visibility = Visibility.Collapsed;
            PosLimitPanel.Visibility = Visibility.Collapsed;
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>
    /// 根据地址中的数据类型决定小数显示格式
    /// DBD = Real (F3), DBW = Int (F0), DBX = Bool
    /// </summary>
    private string GetFormat(string address)
    {
        if (string.IsNullOrEmpty(address)) return "F1";
        if (address.Contains("DBD", StringComparison.OrdinalIgnoreCase)) return "F3";
        if (address.Contains("DBW", StringComparison.OrdinalIgnoreCase)) return "F0";
        return "F1";
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            // 当前位置（只读）
            if (!string.IsNullOrEmpty(_config.CurrentPositionAddress))
            {
                var pos = await _plcService.ReadAutoAsync(_config.CurrentPositionAddress);
                if (pos != null)
                    PositionText.Text = Convert.ToDouble(pos).ToString(GetFormat(_config.CurrentPositionAddress));
            }

            // 点动速度（读写）
            if (!_editingFields.Contains("JogSpeed") && !string.IsNullOrEmpty(_config.JogSpeedAddress))
            {
                var val = await _plcService.ReadAutoAsync(_config.JogSpeedAddress);
                if (val != null)
                    JogSpeedBox.Text = Convert.ToDouble(val).ToString(GetFormat(_config.JogSpeedAddress));
            }

            // 目标位置（读写）
            if (!_editingFields.Contains("TargetPos") && !string.IsNullOrEmpty(_config.ManualPositionAddress))
            {
                var val = await _plcService.ReadAutoAsync(_config.ManualPositionAddress);
                if (val != null)
                    TargetPosBox.Text = Convert.ToDouble(val).ToString(GetFormat(_config.ManualPositionAddress));
            }

            // 定位速度（读写）
            if (!_editingFields.Contains("MoveSpeed") && !string.IsNullOrEmpty(_config.ManualSpeedAddress))
            {
                var val = await _plcService.ReadAutoAsync(_config.ManualSpeedAddress);
                if (val != null)
                    MoveSpeedBox.Text = Convert.ToDouble(val).ToString(GetFormat(_config.ManualSpeedAddress));
            }

            // 限位状态
            if (_config.AxisType == AxisType.Linear)
            {
                await UpdateIndicator(NegLimitDot, _config.NegativeLimitAddress);
                await UpdateIndicator(PosLimitDot, _config.PositiveLimitAddress);
            }
            await UpdateIndicator(OriginDot, _config.OriginAddress);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AxisControl 定时读取异常: {ex.Message}"); }
    }

    private async Task UpdateIndicator(System.Windows.Shapes.Ellipse dot, string address)
    {
        if (string.IsNullOrEmpty(address)) return;
        try
        {
            var val = await _plcService.ReadAutoAsync(address);
            dot.Fill = (val is true || val is 1) ? Green : Gray;
        }
        catch (Exception ex) { dot.Fill = Gray; System.Diagnostics.Debug.WriteLine($"指示灯读取异常 [{address}]: {ex.Message}"); }
    }

    // ========== 读写字段：聚焦停读、Enter 写入 ==========

    private void OnValueBoxFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is string field)
        {
            _editingFields.Add(field);
            tb.BorderBrush = Green;
            tb.SelectAll();
        }
    }

    private void OnValueBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is string field)
        {
            _editingFields.Remove(field);
            tb.BorderBrush = BorderDefault;
        }
    }

    private async void OnValueBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is not TextBox tb || tb.Tag is not string field) return;
        if (!float.TryParse(tb.Text, out var value)) return;

        try
        {
            string? addr = field switch
            {
                "JogSpeed" => _config.JogSpeedAddress,
                "TargetPos" => _config.ManualPositionAddress,
                "MoveSpeed" => _config.ManualSpeedAddress,
                _ => null
            };

            if (!string.IsNullOrEmpty(addr))
                await _plcService.WriteAutoAsync(addr, value);

            _editingFields.Remove(field);
            tb.BorderBrush = BorderDefault;
            Keyboard.ClearFocus();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"字段写入异常: {ex.Message}"); }
    }

    // ========== 控制操作 ==========

    private async void OnEnableClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_config.ServoEnableAddress)) return;
        try
        {
            _isEnabled = !_isEnabled;
            await _plcService.WriteAutoAsync(_config.ServoEnableAddress, _isEnabled);
            EnableBtn.Content = _isEnabled ? "⚡ 使能 ON" : "⚡ 使能";
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"使能切换异常: {ex.Message}"); }
    }

    private static readonly SolidColorBrush BtnDefault = new(Color.FromRgb(0xE8, 0xEA, 0xED));
    private static readonly SolidColorBrush BtnForwardActive = new(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly SolidColorBrush BtnBackwardActive = new(Color.FromRgb(0xE6, 0x51, 0x00));
    private static readonly SolidColorBrush TextDark = new(Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly SolidColorBrush TextWhite = new(Colors.White);

    private async void OnForwardDown(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrEmpty(_config.ForwardAddress)) return;
        (sender as FrameworkElement)?.CaptureMouse();
        ForwardBtn.Background = BtnForwardActive;
        SetChildText(ForwardBtn, TextWhite);
        try { await _plcService.WriteAutoAsync(_config.ForwardAddress, true); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"正转按下异常: {ex.Message}"); }
    }

    private async void OnForwardUp(object sender, MouseButtonEventArgs e)
    {
        (sender as FrameworkElement)?.ReleaseMouseCapture();
        ForwardBtn.Background = BtnDefault;
        SetChildText(ForwardBtn, TextDark);
        if (string.IsNullOrEmpty(_config.ForwardAddress)) return;
        try { await _plcService.WriteAutoAsync(_config.ForwardAddress, false); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"正转释放异常: {ex.Message}"); }
    }

    private async void OnBackwardDown(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrEmpty(_config.BackwardAddress)) return;
        (sender as FrameworkElement)?.CaptureMouse();
        BackwardBtn.Background = BtnBackwardActive;
        SetChildText(BackwardBtn, TextWhite);
        try { await _plcService.WriteAutoAsync(_config.BackwardAddress, true); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"反转按下异常: {ex.Message}"); }
    }

    private async void OnBackwardUp(object sender, MouseButtonEventArgs e)
    {
        (sender as FrameworkElement)?.ReleaseMouseCapture();
        BackwardBtn.Background = BtnDefault;
        SetChildText(BackwardBtn, TextDark);
        if (string.IsNullOrEmpty(_config.BackwardAddress)) return;
        try { await _plcService.WriteAutoAsync(_config.BackwardAddress, false); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"反转释放异常: {ex.Message}"); }
    }

    /// <summary>
    /// 设置 Border 内子 TextBlock 的前景色
    /// </summary>
    private static void SetChildText(Border border, SolidColorBrush brush)
    {
        if (border.Child is TextBlock tb) tb.Foreground = brush;
    }

    private async void OnHomeClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_config.HomeAddress)) return;
        try
        {
            await _plcService.WriteAutoAsync(_config.HomeAddress, true);
            await Task.Delay(500);
            await _plcService.WriteAutoAsync(_config.HomeAddress, false);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"回零异常: {ex.Message}"); }
    }

    private async void OnManualMoveDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(_config.ManualSpeedAddress) && float.TryParse(MoveSpeedBox.Text, out var speed))
                await _plcService.WriteAutoAsync(_config.ManualSpeedAddress, speed);

            if (!string.IsNullOrEmpty(_config.ManualPositionAddress) && float.TryParse(TargetPosBox.Text, out var pos))
                await _plcService.WriteAutoAsync(_config.ManualPositionAddress, pos);

            // 500ms 脉冲触发
            if (!string.IsNullOrEmpty(_config.ManualTriggerAddress))
            {
                await _plcService.WriteAutoAsync(_config.ManualTriggerAddress, true);
                await Task.Delay(500);
                await _plcService.WriteAutoAsync(_config.ManualTriggerAddress, false);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"手动定位失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
