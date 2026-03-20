using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using S7WpfApp.Models;
using S7WpfApp.Services;

namespace S7WpfApp.ViewModels;

/// <summary>
/// 主页面 ViewModel - 用户登录和 PLC 连接配置
/// </summary>
public partial class MainViewModel : BaseViewModel
{
    private readonly IPlcService _plcService;
    private readonly IAuthService _authService;
    private readonly UserConfigService _userConfigService = new();

    #region 用户登录属性

    /// <summary>
    /// 用户名
    /// </summary>
    [ObservableProperty]
    private string _username = string.Empty;

    /// <summary>
    /// 密码
    /// </summary>
    [ObservableProperty]
    private string _password = string.Empty;

    /// <summary>
    /// 是否记住密码
    /// </summary>
    [ObservableProperty]
    private bool _rememberMe;

    /// <summary>
    /// 是否已登录
    /// </summary>
    [ObservableProperty]
    private bool _isLoggedIn;

    /// <summary>
    /// 登录状态信息
    /// </summary>
    [ObservableProperty]
    private string _loginStatus = string.Empty;

    #endregion

    #region PLC 连接属性

    /// <summary>
    /// PLC 连接配置
    /// </summary>
    [ObservableProperty]
    private PlcConnectionConfig _connectionConfig = new();

    /// <summary>
    /// 是否已连接
    /// </summary>
    [ObservableProperty]
    private bool _isConnected;

    /// <summary>
    /// 连接状态文本
    /// </summary>
    [ObservableProperty]
    private string _connectionStatus = "未连接";

    /// <summary>
    /// 连接延迟 (ms)
    /// </summary>
    [ObservableProperty]
    private long _latency;

    /// <summary>
    /// 自动重连
    /// </summary>
    [ObservableProperty]
    private bool _autoReconnect = true;

    /// <summary>
    /// 调试日志
    /// </summary>
    [ObservableProperty]
    private string _logText = "";

    #endregion

    public MainViewModel(IPlcService plcService, IAuthService authService)
    {
        _plcService = plcService;
        _authService = authService;
        Title = "S7 PLC Monitor";

        // 绑定初始值
        AutoReconnect = _plcService.AutoReconnect;

        // 订阅 PLC 事件
        _plcService.ConnectionStatusChanged += OnConnectionStatusChanged;
        _plcService.LatencyUpdated += OnLatencyUpdated;
        _plcService.ErrorOccurred += OnPlcError;
        _plcService.LogMessage += OnLogMessage;

        // 加载保存的登录信息和连接配置
        LoadSavedCredentials();
        LoadSavedConnectionConfig();
    }

    private void OnLogMessage(object? sender, string message)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            // 保留最近50行日志
            var lines = LogText.Split('\n').ToList();
            lines.Add(message);
            if (lines.Count > 50)
            {
                lines = lines.Skip(lines.Count - 50).ToList();
            }
            LogText = string.Join("\n", lines);
        });
    }

    partial void OnAutoReconnectChanged(bool value)
    {
        _plcService.AutoReconnect = value;
    }

    private void OnLatencyUpdated(object? sender, long latency)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Latency = latency;
        });
    }

    #region 用户登录方法

    /// <summary>
    /// 加载保存的登录凭据
    /// </summary>
    private void LoadSavedCredentials()
    {
        try
        {
            var savedUsername = AppSettings.Get("Username", string.Empty);
            var savedPassword = AppSettings.Get("Password", string.Empty);
            var rememberMe = AppSettings.Get("RememberMe", false);

            if (rememberMe && !string.IsNullOrEmpty(savedUsername))
            {
                Username = savedUsername;
                Password = savedPassword;
                RememberMe = true;
            }
        }
        catch
        {
            // 忽略加载错误
        }
    }

    /// <summary>
    /// 保存登录凭据
    /// </summary>
    private void SaveCredentials()
    {
        try
        {
            if (RememberMe)
            {
                AppSettings.Set("Username", Username);
                AppSettings.Set("Password", Password);
                AppSettings.Set("RememberMe", true);
            }
            else
            {
                AppSettings.Remove("Username");
                AppSettings.Remove("Password");
                AppSettings.Set("RememberMe", false);
            }
        }
        catch
        {
            // 忽略保存错误
        }
    }

    /// <summary>
    /// 加载保存的连接配置
    /// </summary>
    private void LoadSavedConnectionConfig()
    {
        try
        {
            var savedIp = AppSettings.Get("PlcIpAddress", "");
            if (!string.IsNullOrEmpty(savedIp))
            {
                ConnectionConfig.IpAddress = savedIp;
            }
            ConnectionConfig.Rack = (short)AppSettings.Get("PlcRack", (int)ConnectionConfig.Rack);
            ConnectionConfig.Slot = (short)AppSettings.Get("PlcSlot", (int)ConnectionConfig.Slot);
            ConnectionConfig.CpuType = (S7CpuType)AppSettings.Get("PlcCpuType", (int)ConnectionConfig.CpuType);
        }
        catch
        {
            // 忽略加载错误
        }
    }

    /// <summary>
    /// 保存连接配置
    /// </summary>
    private void SaveConnectionConfig()
    {
        try
        {
            AppSettings.Set("PlcIpAddress", ConnectionConfig.IpAddress);
            AppSettings.Set("PlcRack", (int)ConnectionConfig.Rack);
            AppSettings.Set("PlcSlot", (int)ConnectionConfig.Slot);
            AppSettings.Set("PlcCpuType", (int)ConnectionConfig.CpuType);
        }
        catch
        {
            // 忽略保存错误
        }
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(Username))
        {
            LoginStatus = "请输入用户名";
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            LoginStatus = "请输入密码";
            return;
        }

        try
        {
            IsBusy = true;
            LoginStatus = "正在登录...";

            // 模拟登录验证（实际应用中可以连接到认证服务器）
            await Task.Delay(500);

            // 简单的本地验证（可以扩展为远程验证）
            if (ValidateCredentials(Username, Password, out bool isAdmin))
            {
                IsLoggedIn = true;
                LoginStatus = isAdmin ? $"欢迎，管理员 {Username}！" : $"欢迎，{Username}！";
                SaveCredentials();
                SetStatus($"用户 {Username} 已登录");

                // 更新 AuthService 的管理员状态
                if (isAdmin)
                {
                    _authService.SetAdminState(true);
                }
            }
            else
            {
                LoginStatus = "用户名或密码错误";
                IsLoggedIn = false;
            }
        }
        catch (Exception ex)
        {
            LoginStatus = $"登录失败: {ex.Message}";
            IsLoggedIn = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 验证用户凭据
    /// </summary>
    private bool ValidateCredentials(string username, string password, out bool isAdmin)
    {
        isAdmin = false;
        // 使用外部配置文件验证用户
        if (_userConfigService.ValidateCredentials(username, password, out var role))
        {
            isAdmin = string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 获取用户配置文件路径
    /// </summary>
    public string UserConfigPath => _userConfigService.ConfigPath;

    /// <summary>
    /// 用户登出
    /// </summary>
    [RelayCommand]
    private void Logout()
    {
        IsLoggedIn = false;
        LoginStatus = string.Empty;
        Password = string.Empty;
        SetStatus("已登出");

        // 重置管理员状态
        _authService.SetAdminState(false);
    }

    #endregion

    #region PLC 连接方法

    private void OnConnectionStatusChanged(object? sender, bool connected)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = connected;
            ConnectionStatus = connected ? "已连接" : "未连接";
            SetStatus(connected ? "已成功连接到 PLC" : "已断开连接");
        });
    }

    private void OnPlcError(object? sender, string errorMessage)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            SetError(errorMessage);
        });
    }

    /// <summary>
    /// 连接到 PLC
    /// </summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            SetStatus("正在连接...");

            var result = await _plcService.ConnectAsync(ConnectionConfig);

            if (result)
            {
                SetStatus($"已连接到 {ConnectionConfig.IpAddress}");
                SaveConnectionConfig();
            }
            else
            {
                SetError("连接失败，请检查 IP 地址和 PLC 配置");
            }
        }
        catch (Exception ex)
        {
            SetError($"连接错误: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            await _plcService.DisconnectAsync();
            SetStatus("已断开连接");
        }
        catch (Exception ex)
        {
            SetError($"断开连接错误: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion
}
