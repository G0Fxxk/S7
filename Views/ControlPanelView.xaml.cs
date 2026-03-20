using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using S7WpfApp.Models;
using S7WpfApp.ViewModels;

namespace S7WpfApp.Views;

public partial class ControlPanelView : UserControl
{
    private readonly ControlPanelViewModel _vm;

    private readonly Services.IPlcService _plcService;
    private readonly Services.ISymbolService _symbolService;
    private readonly Services.IAuthService _authService;
    private readonly Services.IAxisConfigService _axisService;

    public ControlPanelView(
        ControlPanelViewModel vm,
        Services.IPlcService plcService,
        Services.ISymbolService symbolService,
        Services.IAuthService authService,
        Services.IAxisConfigService axisService)
    {
        _vm = vm;
        _plcService = plcService;
        _symbolService = symbolService;
        _authService = authService;
        _axisService = axisService;
        InitializeComponent();
        GroupsList.ItemsSource = _vm.Groups;
    }

    private async void OnAddControlClick(object s, RoutedEventArgs e)
        => await _vm.AddControlCommand.ExecuteAsync(null);

    private async void OnAddAxisClick(object s, RoutedEventArgs e)
        => await _vm.AddAxisControlGroupCommand.ExecuteAsync(null);

    private async void OnImportClick(object s, RoutedEventArgs e)
        => await _vm.ImportConfigCommand.ExecuteAsync(null);

    private async void OnExportClick(object s, RoutedEventArgs e)
        => await _vm.ExportConfigCommand.ExecuteAsync(null);

    private void OnOpenTrendClick(object s, RoutedEventArgs e)
    {
        var multiTrendWindow = System.Windows.Application.Current.Windows
            .OfType<MultiTrendWindow>()
            .FirstOrDefault();

        if (multiTrendWindow == null)
        {
            multiTrendWindow = new MultiTrendWindow(
                App.Services.GetRequiredService<MultiTrendViewModel>())
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            multiTrendWindow.Show();
        }
        else
        {
            if (multiTrendWindow.WindowState == WindowState.Minimized)
                multiTrendWindow.WindowState = WindowState.Normal;
            multiTrendWindow.Activate();
        }
    }

    private static readonly System.Windows.Media.SolidColorBrush BtnGray =
        new(System.Windows.Media.Color.FromRgb(0xE8, 0xEA, 0xED));
    private static readonly System.Windows.Media.SolidColorBrush BtnGreen =
        new(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));

    private async void OnControlClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ControlItem item) return;

        switch (item.Binding.Type)
        {
            case BindingType.ToggleButton:
                await _vm.ToggleCommand.ExecuteAsync(item);
                // 切换 ON/OFF 显示
                UpdateToggleButton(btn, item);
                break;

            case BindingType.NumericInput:
                var value = await Services.UIHelper.DisplayPrompt(
                    "输入数值", $"请输入 {item.Binding.Name} 的新值:",
                    initialValue: item.CurrentValue?.ToString());
                if (!string.IsNullOrEmpty(value))
                {
                    item.InputValue = value;
                    await _vm.WriteNumericCommand.ExecuteAsync(item);
                }
                break;

            case BindingType.MomentaryButton:
                // 点动按钮不在 Click 中处理（由 MouseDown/MouseUp 处理）
                break;

            default:
                // 检查是否是轴控件
                if (item.Binding.DataType == "AxisControl" && !string.IsNullOrEmpty(item.Binding.SymbolName))
                {
                    if (_authService.IsAdmin)
                    {
                        var action = await Services.UIHelper.DisplayActionSheet(
                            "轴操作", "取消", null,
                            "🎮 打开控制面板", "✏️ 编辑符号变量");
                        if (action == "🎮 打开控制面板")
                            await _vm.OpenAxisControlCommand.ExecuteAsync(item.Binding.SymbolName);
                        else if (action == "✏️ 编辑符号变量")
                            await EditAxisSymbolsAsync(item.Binding.SymbolName);
                    }
                    else
                    {
                        await _vm.OpenAxisControlCommand.ExecuteAsync(item.Binding.SymbolName);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// 点动按钮按下 → 绿色背景 + 写 true
    /// </summary>
    private async void OnControlMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ControlItem item) return;
        if (item.Binding.Type != BindingType.MomentaryButton) return;

        e.Handled = true;
        btn.CaptureMouse();
        btn.Background = BtnGreen;
        btn.Foreground = System.Windows.Media.Brushes.White;
        btn.Content = "● 按下中";
        await _vm.MomentaryPressCommand.ExecuteAsync(item);
    }

    /// <summary>
    /// 点动按钮松开 → 灰色背景 + 写 false
    /// </summary>
    private async void OnControlMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ControlItem item) return;
        if (item.Binding.Type != BindingType.MomentaryButton) return;

        btn.ReleaseMouseCapture();
        btn.Background = BtnGray;
        btn.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("TextPrimaryBrush");
        btn.Content = "⚡ 操作";
        await _vm.MomentaryReleaseCommand.ExecuteAsync(item);
    }

    /// <summary>
    /// 更新保持按钮的 ON/OFF 显示
    /// </summary>
    private void UpdateToggleButton(Button btn, ControlItem item)
    {
        var isOn = item.CurrentValue is true or 1 or "1" or "True";
        btn.Background = isOn ? BtnGreen : BtnGray;
        btn.Foreground = isOn
            ? System.Windows.Media.Brushes.White
            : (System.Windows.Media.SolidColorBrush)FindResource("TextPrimaryBrush");
        btn.Content = isOn ? "● ON" : "○ OFF";
    }

    /// <summary>
    /// 卡片加载时根据类型初始化：状态指示隐藏操作按钮，保持按钮设初始状态
    /// </summary>
    private void OnCardLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border card || card.Tag is not ControlItem item) return;

        // 在 VisualTree 中找到 ActionBtn
        var actionBtn = FindVisualChild<Button>(card, "ActionBtn");
        if (actionBtn == null) return;

        switch (item.Binding.Type)
        {
            case BindingType.StatusIndicator when item.Binding.DataType != "AxisControl":
                // 纯状态指示：隐藏操作按钮
                actionBtn.Visibility = Visibility.Collapsed;
                break;

            case BindingType.ToggleButton:
                // 保持按钮：初始化 ON/OFF 显示
                UpdateToggleButton(actionBtn, item);
                break;
        }
    }

    /// <summary>
    /// 在 VisualTree 中按名称查找子控件
    /// </summary>
    private static T? FindVisualChild<T>(System.Windows.DependencyObject parent, string name)
        where T : FrameworkElement
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T fe && fe.Name == name) return fe;
            var result = FindVisualChild<T>(child, name);
            if (result != null) return result;
        }
        return null;
    }

    private async void OnDeleteControlClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ControlItem item) return;

        // 轴控件删除
        if (item.Binding.DataType == "AxisControl" && !string.IsNullOrEmpty(item.Binding.SymbolName))
        {
            await _vm.DeleteAxisConfigCommand.ExecuteAsync(item.Binding.SymbolName);
            GroupsList.ItemsSource = null;
            GroupsList.ItemsSource = _vm.Groups;
            return;
        }

        // 普通控件删除
        var confirm = await Services.UIHelper.DisplayConfirm(
            "删除控件", $"确定要删除 \"{item.Binding.Name}\" 吗？", "删除", "取消");

        if (confirm)
        {
            await _vm.DeleteControlCommand.ExecuteAsync(item);
            GroupsList.ItemsSource = null;
            GroupsList.ItemsSource = _vm.Groups;
        }
    }
    /// <summary>
    /// 管理员编辑轴符号变量 — 复用 AxisConfigWindow
    /// </summary>
    private Task EditAxisSymbolsAsync(string axisId)
    {
        var config = _axisService.GetById(axisId);
        if (config == null) return Task.CompletedTask;

        var configWin = new AxisConfigWindow(config, _symbolService);
        configWin.Owner = System.Windows.Application.Current.MainWindow;
        if (configWin.ShowDialog() == true && configWin.Result != null)
        {
            var result = configWin.Result;
            // 保留原始 ID、名称、分组等信息
            result.Id = config.Id;
            result.Name = config.Name;
            result.Group = config.Group;
            result.DbNumber = config.DbNumber;
            result.AxisType = config.AxisType;

            _axisService.Save(result);
            _vm.LoadBindingsCommand.Execute(null);
            GroupsList.ItemsSource = null;
            GroupsList.ItemsSource = _vm.Groups;
        }

        return Task.CompletedTask;
    }
}
