using System.IO;
using System.Text.Json;

namespace S7WpfApp.Services;

/// <summary>
/// 应用设置服务 - 替代 MAUI Preferences，使用 JSON 文件持久化
/// </summary>
public static class AppSettings
{
    private static readonly string _settingsPath;
    private static Dictionary<string, JsonElement> _data = new();
    private static readonly object _lock = new();

    static AppSettings()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "S7WpfApp");
        Directory.CreateDirectory(appData);
        _settingsPath = Path.Combine(appData, "settings.json");
        Load();
    }

    private static void Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                _data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
            }
        }
        catch (Exception ex) { _data = new(); System.Diagnostics.Debug.WriteLine($"设置加载失败，使用默认值: {ex.Message}"); }
    }

    private static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"设置保存失败: {ex.Message}"); }
    }

    public static string Get(string key, string defaultValue)
    {
        lock (_lock)
        {
            if (_data.TryGetValue(key, out var val))
                return val.GetString() ?? defaultValue;
            return defaultValue;
        }
    }

    public static int Get(string key, int defaultValue)
    {
        lock (_lock)
        {
            if (_data.TryGetValue(key, out var val) && val.TryGetInt32(out var i))
                return i;
            return defaultValue;
        }
    }

    public static bool Get(string key, bool defaultValue)
    {
        lock (_lock)
        {
            if (_data.TryGetValue(key, out var val))
            {
                if (val.ValueKind == JsonValueKind.True) return true;
                if (val.ValueKind == JsonValueKind.False) return false;
            }
            return defaultValue;
        }
    }

    public static void Set(string key, string value)
    {
        lock (_lock)
        {
            _data[key] = JsonSerializer.SerializeToElement(value);
            Save();
        }
    }

    public static void Set(string key, int value)
    {
        lock (_lock)
        {
            _data[key] = JsonSerializer.SerializeToElement(value);
            Save();
        }
    }

    public static void Set(string key, bool value)
    {
        lock (_lock)
        {
            _data[key] = JsonSerializer.SerializeToElement(value);
            Save();
        }
    }

    public static void Remove(string key)
    {
        lock (_lock)
        {
            _data.Remove(key);
            Save();
        }
    }
}
