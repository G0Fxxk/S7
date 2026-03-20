using System.IO;
using System.Text.Json;

namespace S7WpfApp.Services;

/// <summary>
/// 用户账户
/// </summary>
public class UserAccount
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "operator";  // admin, operator, guest
}

/// <summary>
/// 用户配置
/// </summary>
public class UserConfiguration
{
    public List<UserAccount> Users { get; set; } = new();
}

/// <summary>
/// 用户配置服务
/// </summary>
public class UserConfigService
{
    private readonly string _configPath;
    private UserConfiguration _config = new();

    public UserConfigService()
    {
        // 配置文件保存在程序目录下
        _configPath = Path.Combine(AppContext.BaseDirectory, "users.json");
        LoadOrCreateConfig();
    }

    /// <summary>
    /// 获取配置文件路径
    /// </summary>
    public string ConfigPath => _configPath;

    /// <summary>
    /// 加载或创建配置
    /// </summary>
    private void LoadOrCreateConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<UserConfiguration>(json) ?? new UserConfiguration();
            }
            else
            {
                // 创建默认配置
                _config = new UserConfiguration
                {
                    Users = new List<UserAccount>
                    {
                        new() { Username = "admin", Password = "admin123", Role = "admin" },
                        new() { Username = "operator", Password = "op123", Role = "operator" },
                        new() { Username = "guest", Password = "guest", Role = "guest" }
                    }
                };
                SaveConfig();
            }
        }
        catch
        {
            // 加载失败时使用默认配置
            _config = new UserConfiguration
            {
                Users = new List<UserAccount>
                {
                    new() { Username = "admin", Password = "admin123", Role = "admin" }
                }
            };
        }
    }

    /// <summary>
    /// 保存配置
    /// </summary>
    public void SaveConfig()
    {
        try
        {
            // 确保目录存在
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_config, options);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存用户配置失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 验证用户凭据
    /// </summary>
    public bool ValidateCredentials(string username, string password, out string? role)
    {
        role = null;
        var user = _config.Users.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase) &&
            u.Password == password);

        if (user != null)
        {
            role = user.Role;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 获取所有用户
    /// </summary>
    public List<UserAccount> GetUsers() => _config.Users;

    /// <summary>
    /// 添加用户
    /// </summary>
    public void AddUser(UserAccount user)
    {
        _config.Users.Add(user);
        SaveConfig();
    }

    /// <summary>
    /// 删除用户
    /// </summary>
    public void RemoveUser(string username)
    {
        _config.Users.RemoveAll(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        SaveConfig();
    }
}
