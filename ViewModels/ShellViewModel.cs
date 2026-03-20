using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using S7WpfApp.Services;

namespace S7WpfApp.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly IPlcService _plcService;
    private readonly IAuthService _authService;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "未连接";

    [ObservableProperty]
    private long _latency;

    [ObservableProperty]
    private bool _isAdmin;

    [ObservableProperty]
    private string _plcCpuStatus = "Unknown";

    public ShellViewModel(IPlcService plcService, IAuthService authService)
    {
        _plcService = plcService;
        _authService = authService;

        IsConnected = _plcService.IsConnected;
        IsAdmin = _authService.IsAdmin;
        ConnectionStatus = _plcService.IsConnected ? "已连接" : "未连接";

        _plcService.ConnectionStatusChanged += OnConnectionStatusChanged;
        _plcService.LatencyUpdated += OnLatencyUpdated;
        _plcService.PlcCpuStatusChanged += OnPlcCpuStatusChanged;
        _authService.AdminLoginStateChanged += OnAdminLoginStateChanged;
    }

    private void OnConnectionStatusChanged(object? sender, bool connected)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = connected;
            ConnectionStatus = connected ? "已连接" : "未连接";
        });
    }

    private void OnLatencyUpdated(object? sender, long latency)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => Latency = latency);
    }

    private void OnPlcCpuStatusChanged(object? sender, string status)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => PlcCpuStatus = status);
    }

    private void OnAdminLoginStateChanged(object? sender, bool isAdmin)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => IsAdmin = isAdmin);
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        var password = await UIHelper.DisplayPrompt(
            "管理员登录",
            "请输入管理员密码：");

        if (string.IsNullOrEmpty(password)) return;

        if (_authService.Login(password))
        {
            await UIHelper.DisplayAlert("成功", "管理员登录成功", "确定");
        }
        else
        {
            await UIHelper.DisplayAlert("错误", "密码错误", "确定");
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        var confirm = await UIHelper.DisplayConfirm("确认", "确定要退出管理员模式吗？", "确定", "取消");
        if (confirm)
        {
            _authService.Logout();
        }
    }
}
