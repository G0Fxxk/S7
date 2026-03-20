using System.IO;
using System.Text.Json;
using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// 控件绑定服务实现
/// </summary>
public class BindingService : IBindingService
{
    private readonly string _configFilePath;
    private List<ControlBinding> _bindings = new();
    private readonly object _lock = new();

    public event EventHandler? BindingsChanged;

    public BindingService()
    {
        // 配置文件存储在应用数据目录
        _configFilePath = Path.Combine(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "S7WpfApp"), "bindings.json");
        LoadBindings();
    }

    /// <summary>
    /// 从本地文件加载绑定配置
    /// </summary>
    private void LoadBindings()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                var config = JsonSerializer.Deserialize<BindingConfiguration>(json);
                if (config?.Bindings != null)
                {
                    _bindings = config.Bindings;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载绑定配置失败: {ex.Message}");
            _bindings = new List<ControlBinding>();
        }
    }

    /// <summary>
    /// 保存绑定配置到本地文件
    /// </summary>
    private void SaveToFile()
    {
        try
        {
            var config = new BindingConfiguration
            {
                Version = "1.0",
                ExportTime = DateTime.Now,
                Bindings = _bindings
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存绑定配置失败: {ex.Message}");
        }
    }

    public List<ControlBinding> GetBindings()
    {
        lock (_lock)
        {
            return _bindings.ToList();
        }
    }

    public List<ControlBinding> GetBindingsByGroup(string group)
    {
        lock (_lock)
        {
            return _bindings.Where(b => b.Group == group).OrderBy(b => b.Order).ToList();
        }
    }

    public List<string> GetGroups()
    {
        lock (_lock)
        {
            return _bindings.Select(b => b.Group).Distinct().OrderBy(g => g).ToList();
        }
    }

    public void SaveBinding(ControlBinding binding)
    {
        lock (_lock)
        {
            // 检查是否已存在
            var existing = _bindings.FirstOrDefault(b => b.Address == binding.Address);
            if (existing != null)
            {
                // 更新现有绑定
                existing.Type = binding.Type;
                existing.Name = binding.Name;
                existing.Group = binding.Group;
                existing.DataType = binding.DataType;
                existing.DbNumber = binding.DbNumber;
                existing.Order = binding.Order;
            }
            else
            {
                // 添加新绑定
                _bindings.Add(binding);
            }

            SaveToFile();
        }

        BindingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateBinding(ControlBinding binding)
    {
        SaveBinding(binding);
    }

    public void RemoveBinding(string address)
    {
        lock (_lock)
        {
            _bindings.RemoveAll(b => b.Address == address);
            SaveToFile();
        }

        BindingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public ControlBinding? GetBindingByAddress(string address)
    {
        lock (_lock)
        {
            return _bindings.FirstOrDefault(b => b.Address == address);
        }
    }

    public async Task<string> ExportAsync(string filePath)
    {
        try
        {
            var config = new BindingConfiguration
            {
                Version = "1.0",
                ExportTime = DateTime.Now,
                Bindings = _bindings.ToList()
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(config, options);
            await File.WriteAllTextAsync(filePath, json);

            return $"成功导出 {_bindings.Count} 个绑定配置";
        }
        catch (Exception ex)
        {
            throw new Exception($"导出失败: {ex.Message}");
        }
    }

    public async Task ImportAsync(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var config = JsonSerializer.Deserialize<BindingConfiguration>(json);

            if (config?.Bindings == null)
            {
                throw new Exception("无效的配置文件格式");
            }

            lock (_lock)
            {
                _bindings = config.Bindings;
                SaveToFile();
            }

            BindingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (JsonException)
        {
            throw new Exception("配置文件格式错误");
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _bindings.Clear();
            SaveToFile();
        }

        BindingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
