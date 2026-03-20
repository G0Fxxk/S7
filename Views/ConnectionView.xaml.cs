using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using S7WpfApp.Models;
using S7WpfApp.ViewModels;

namespace S7WpfApp.Views;

public partial class ConnectionView : UserControl
{
    private readonly MainViewModel _vm;

    public ConnectionView(MainViewModel vm)
    {
        // 必须先初始化 ViewModel，XAML 解析时会触发事件回调
        _vm = vm;
        InitializeComponent();

        // 初始化 UI 从 ViewModel
        IpBox.Text = _vm.ConnectionConfig.IpAddress;
        RackBox.Text = _vm.ConnectionConfig.Rack.ToString();
        SlotBox.Text = _vm.ConnectionConfig.Slot.ToString();
        AutoReconnectCheck.IsChecked = _vm.AutoReconnect;

        // 选择正确的 CPU 类型
        foreach (ComboBoxItem item in CpuTypeCombo.Items)
        {
            if (item.Tag?.ToString() == ((int)_vm.ConnectionConfig.CpuType).ToString())
            {
                CpuTypeCombo.SelectedItem = item;
                break;
            }
        }

        // 用户名
        UsernameBox.Text = _vm.Username;
        PasswordBox.Password = _vm.Password;
        RememberCheck.IsChecked = _vm.RememberMe;

        // 订阅 ViewModel 属性变更
        _vm.PropertyChanged += (s, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(_vm.IsConnected):
                        UpdateConnectionUI();
                        break;
                    case nameof(_vm.ConnectionStatus):
                        ConnectionStatusText.Text = _vm.ConnectionStatus;
                        break;
                    case nameof(_vm.Latency):
                        LatencyText.Text = $"延迟: {_vm.Latency}ms";
                        break;
                    case nameof(_vm.LogText):
                        // 日志面板已移除
                        break;
                    case nameof(_vm.LoginStatus):
                        LoginStatusText.Text = _vm.LoginStatus;
                        break;
                    case nameof(_vm.IsLoggedIn):
                        UpdateLoginUI();
                        break;
                }
            });
        };

        // 初始 UI 状态
        UpdateConnectionUI();
        UpdateLoginUI();
    }

    private void UpdateConnectionUI()
    {
        if (_vm.IsConnected)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xaa));
            ConnectBtn.IsEnabled = false;
            DisconnectBtn.IsEnabled = true;
            IpBox.IsEnabled = false;
            CpuTypeCombo.IsEnabled = false;
            RackBox.IsEnabled = false;
            SlotBox.IsEnabled = false;
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xff, 0x6b, 0x6b));
            ConnectBtn.IsEnabled = true;
            DisconnectBtn.IsEnabled = false;
            IpBox.IsEnabled = true;
            CpuTypeCombo.IsEnabled = true;
            RackBox.IsEnabled = true;
            SlotBox.IsEnabled = true;
        }
    }

    private void UpdateLoginUI()
    {
        if (_vm.IsLoggedIn)
        {
            LoginBtn.Visibility = Visibility.Collapsed;
            LogoutBtn.Visibility = Visibility.Visible;
            UsernameBox.IsEnabled = false;
            PasswordBox.IsEnabled = false;
        }
        else
        {
            LoginBtn.Visibility = Visibility.Visible;
            LogoutBtn.Visibility = Visibility.Collapsed;
            UsernameBox.IsEnabled = true;
            PasswordBox.IsEnabled = true;
        }
    }

    private void SyncConfigFromUI()
    {
        _vm.ConnectionConfig.IpAddress = IpBox.Text.Trim();
        if (short.TryParse(RackBox.Text, out var rack)) _vm.ConnectionConfig.Rack = rack;
        if (short.TryParse(SlotBox.Text, out var slot)) _vm.ConnectionConfig.Slot = slot;

        if (CpuTypeCombo.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            if (int.TryParse(item.Tag.ToString(), out var cpuType))
                _vm.ConnectionConfig.CpuType = (S7CpuType)cpuType;
        }
    }

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        SyncConfigFromUI();
        await _vm.ConnectCommand.ExecuteAsync(null);
    }

    private async void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        await _vm.DisconnectCommand.ExecuteAsync(null);
    }

    private void OnAutoReconnectChanged(object sender, RoutedEventArgs e)
    {
        _vm.AutoReconnect = AutoReconnectCheck.IsChecked == true;
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        _vm.Username = UsernameBox.Text;
        _vm.Password = PasswordBox.Password;
        _vm.RememberMe = RememberCheck.IsChecked == true;
        await _vm.LoginCommand.ExecuteAsync(null);
    }

    private void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        _vm.LogoutCommand.Execute(null);
    }
}
