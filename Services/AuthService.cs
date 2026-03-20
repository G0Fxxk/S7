using S7WpfApp.Services;
namespace S7WpfApp.Services;

/// <summary>
/// 认证服务实现
/// </summary>
public class AuthService : IAuthService
{
    // Admin 密码（可通过配置修改）
    private const string AdminPassword = "admin123";
    private const string IsAdminKey = "IsAdmin";

    private bool _isAdmin;

    public bool IsAdmin
    {
        get => _isAdmin;
        private set
        {
            if (_isAdmin != value)
            {
                _isAdmin = value;
                AppSettings.Set(IsAdminKey, value);
                AdminLoginStateChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<bool>? AdminLoginStateChanged;

    public AuthService()
    {
        // 从持久化存储恢复登录状态
        _isAdmin = AppSettings.Get(IsAdminKey, false);
    }

    public bool Login(string password)
    {
        if (password == AdminPassword)
        {
            IsAdmin = true;
            return true;
        }
        return false;
    }

    public void Logout()
    {
        IsAdmin = false;
    }

    public void SetAdminState(bool isAdmin)
    {
        IsAdmin = isAdmin;
    }
}
