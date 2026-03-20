using S7WpfApp.Services;
using System.Text.Json;
using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// 轴配置服务接口
/// </summary>
public interface IAxisConfigService
{
    /// <summary>
    /// 获取所有轴配置
    /// </summary>
    List<AxisConfig> GetAll();

    /// <summary>
    /// 根据 ID 获取轴配置
    /// </summary>
    AxisConfig? GetById(string id);

    /// <summary>
    /// 保存轴配置
    /// </summary>
    void Save(AxisConfig config);

    /// <summary>
    /// 删除轴配置
    /// </summary>
    void Delete(string id);
}

/// <summary>
/// 轴配置服务实现
/// </summary>
public class AxisConfigService : IAxisConfigService
{
    private const string StorageKey = "AxisConfigs";
    private List<AxisConfig> _configs = new();

    public AxisConfigService()
    {
        Load();
    }

    public List<AxisConfig> GetAll() => _configs.ToList();

    public AxisConfig? GetById(string id) => _configs.FirstOrDefault(c => c.Id == id);

    public void Save(AxisConfig config)
    {
        var existing = _configs.FirstOrDefault(c => c.Id == config.Id);
        if (existing != null)
        {
            _configs.Remove(existing);
        }
        _configs.Add(config);
        Persist();
    }

    public void Delete(string id)
    {
        var config = _configs.FirstOrDefault(c => c.Id == id);
        if (config != null)
        {
            _configs.Remove(config);
            Persist();
        }
    }

    private void Load()
    {
        try
        {
            var json = AppSettings.Get(StorageKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                _configs = JsonSerializer.Deserialize<List<AxisConfig>>(json) ?? new();
            }
        }
        catch
        {
            _configs = new();
        }
    }

    private void Persist()
    {
        var json = JsonSerializer.Serialize(_configs);
        AppSettings.Set(StorageKey, json);
    }
}
