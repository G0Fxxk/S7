using System.IO;
using System.Text.Json;

namespace S7WpfApp.Services;

/// <summary>
/// 已解析的 DB 文件条目
/// </summary>
public class DbFileEntry
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// DB 文件完整路径
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// 文件名（用于显示）
    /// </summary>
    public string FileName { get; set; } = "";

    /// <summary>
    /// DB 编号
    /// </summary>
    public int DbNumber { get; set; }

    /// <summary>
    /// DB 名称（从文件解析出的）
    /// </summary>
    public string DbName { get; set; } = "";

    /// <summary>
    /// 变量数量
    /// </summary>
    public int VariableCount { get; set; }

    /// <summary>
    /// 首次导入时间
    /// </summary>
    public DateTime FirstImportTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdateTime { get; set; } = DateTime.Now;
}

/// <summary>
/// DB 文件管理服务接口
/// </summary>
public interface IDbFileService
{
    /// <summary>
    /// 获取所有已保存的 DB 文件条目
    /// </summary>
    IEnumerable<DbFileEntry> GetAll();

    /// <summary>
    /// 添加或更新 DB 文件条目
    /// </summary>
    void AddOrUpdate(DbFileEntry entry);

    /// <summary>
    /// 根据 DB 编号获取条目
    /// </summary>
    DbFileEntry? GetByDbNumber(int dbNumber);

    /// <summary>
    /// 删除 DB 文件条目
    /// </summary>
    void Remove(string id);

    /// <summary>
    /// 保存到文件
    /// </summary>
    Task SaveAsync();

    /// <summary>
    /// 从文件加载
    /// </summary>
    Task LoadAsync();
}

/// <summary>
/// DB 文件管理服务实现
/// </summary>
public class DbFileService : IDbFileService
{
    private readonly List<DbFileEntry> _entries = new();
    private readonly string _configPath;

    public DbFileService()
    {
        _configPath = Path.Combine(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "S7WpfApp"), "dbfiles.json");
    }

    public IEnumerable<DbFileEntry> GetAll() => _entries.Where(e => e != null).OrderByDescending(e => e.LastUpdateTime);

    public void AddOrUpdate(DbFileEntry entry)
    {
        var existing = _entries.FirstOrDefault(e => e.DbNumber == entry.DbNumber);
        if (existing != null)
        {
            // 更新现有条目
            existing.FilePath = entry.FilePath;
            existing.FileName = entry.FileName;
            existing.DbName = entry.DbName;
            existing.VariableCount = entry.VariableCount;
            existing.LastUpdateTime = DateTime.Now;
        }
        else
        {
            // 添加新条目
            _entries.Add(entry);
        }
    }

    public DbFileEntry? GetByDbNumber(int dbNumber) =>
        _entries.FirstOrDefault(e => e.DbNumber == dbNumber);

    public void Remove(string id)
    {
        var entry = _entries.FirstOrDefault(e => e.Id == id);
        if (entry != null)
        {
            _entries.Remove(entry);
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_configPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存 DB 文件列表失败: {ex.Message}");
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = await File.ReadAllTextAsync(_configPath);
                var entries = JsonSerializer.Deserialize<List<DbFileEntry?>>(json);
                if (entries != null)
                {
                    _entries.Clear();
                    _entries.AddRange(entries.Where(e => e != null)!);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载 DB 文件列表失败: {ex.Message}");
        }
    }
}
