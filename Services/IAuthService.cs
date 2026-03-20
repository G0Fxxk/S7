namespace S7WpfApp.Services;

/// <summary>
/// 认证服务接口
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// 是否为管理员
    /// </summary>
    bool IsAdmin { get; }

    /// <summary>
    /// 管理员登录状态变化事件
    /// </summary>
    event EventHandler<bool>? AdminLoginStateChanged;

    /// <summary>
    /// 登录
    /// </summary>
    /// <param name="password">密码</param>
    /// <returns>是否成功</returns>
    bool Login(string password);

    /// <summary>
    /// 登出
    /// </summary>
    void Logout();

    /// <summary>
    /// 设置管理员状态（由外部登录系统调用）
    /// </summary>
    void SetAdminState(bool isAdmin);
}
