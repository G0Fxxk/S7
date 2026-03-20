using System.IO;
using System.Text.Json;
using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// 符号服务实现 - 支持自定义符号配置和持久化存储
/// </summary>
public class SymbolService : ISymbolService
{
    private readonly IDbBlockService _dbBlockService;
    private readonly string _configFilePath;
    private readonly Dictionary<string, SymbolEntry> _symbolByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SymbolEntry> _symbolByAddress = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public event EventHandler? SymbolTableChanged;

    public SymbolService(IDbBlockService dbBlockService)
    {
        _dbBlockService = dbBlockService;
        _configFilePath = Path.Combine(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "S7WpfApp"), "symbols.json");

        // 异步加载配置
        _ = LoadAsync();
    }

    /// <inheritdoc/>
    public string? GetAddress(string symbolName)
    {
        lock (_lock)
        {
            return _symbolByName.TryGetValue(symbolName, out var entry) ? entry.Address : null;
        }
    }

    /// <inheritdoc/>
    public string? GetSymbolName(string address)
    {
        lock (_lock)
        {
            return _symbolByAddress.TryGetValue(address, out var entry) ? entry.Name : null;
        }
    }

    /// <inheritdoc/>
    public IEnumerable<SymbolEntry> GetAllSymbols()
    {
        lock (_lock)
        {
            return _symbolByName.Values.ToList();
        }
    }

    /// <inheritdoc/>
    public IEnumerable<SymbolEntry> SearchSymbols(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return GetAllSymbols();
        }

        var lowerKeyword = keyword.ToLower();
        lock (_lock)
        {
            return _symbolByName.Values
                .Where(s => s.Name.ToLower().Contains(lowerKeyword) ||
                            s.Address.ToLower().Contains(lowerKeyword) ||
                            s.Comment.ToLower().Contains(lowerKeyword) ||
                            s.Category.ToLower().Contains(lowerKeyword))
                .ToList();
        }
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetCategories()
    {
        lock (_lock)
        {
            return _symbolByName.Values
                .Select(s => string.IsNullOrEmpty(s.Category) ? "未分类" : s.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToList();
        }
    }

    /// <inheritdoc/>
    public IEnumerable<SymbolEntry> GetSymbolsByCategory(string category)
    {
        lock (_lock)
        {
            return _symbolByName.Values
                .Where(s => (string.IsNullOrEmpty(s.Category) ? "未分类" : s.Category) == category)
                .ToList();
        }
    }

    /// <inheritdoc/>
    public void AddSymbol(string name, string address, string dataType, string originalSymbolPath = "", int dbNumber = 0, string dbFilePath = "", string comment = "", string category = "")
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
            return;

        lock (_lock)
        {
            var entry = new SymbolEntry
            {
                Name = name,
                Address = address,
                DataType = dataType,
                OriginalSymbolPath = string.IsNullOrEmpty(originalSymbolPath) ? name : originalSymbolPath,
                DbNumber = dbNumber,
                DbFilePath = dbFilePath,
                Comment = comment,
                Category = category
            };

            _symbolByName[name] = entry;
            _symbolByAddress[address] = entry;
        }

        _ = SaveAsync();
        SymbolTableChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void UpdateSymbol(string oldName, string newName, string address, string dataType, string comment = "")
    {
        lock (_lock)
        {
            // 移除旧的
            if (_symbolByName.TryGetValue(oldName, out var oldEntry))
            {
                _symbolByName.Remove(oldName);
                _symbolByAddress.Remove(oldEntry.Address);
            }

            // 添加新的
            var entry = new SymbolEntry
            {
                Name = newName,
                Address = address,
                DataType = dataType,
                Comment = comment
            };

            _symbolByName[newName] = entry;
            _symbolByAddress[address] = entry;
        }

        _ = SaveAsync();
        SymbolTableChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void RemoveSymbol(string name)
    {
        lock (_lock)
        {
            if (_symbolByName.TryGetValue(name, out var entry))
            {
                _symbolByName.Remove(name);
                _symbolByAddress.Remove(entry.Address);
            }
        }

        _ = SaveAsync();
        SymbolTableChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public bool SymbolExists(string name)
    {
        lock (_lock)
        {
            return _symbolByName.ContainsKey(name);
        }
    }

    /// <inheritdoc/>
    public bool AddressHasSymbol(string address)
    {
        lock (_lock)
        {
            return _symbolByAddress.ContainsKey(address);
        }
    }

    /// <inheritdoc/>
    public void RefreshFromDbBlocks()
    {
        // 从 DbBlockService 获取变量并合并到符号表（可选功能）
        var dbBlocks = _dbBlockService.GetAllDbBlocks();

        lock (_lock)
        {
            foreach (var db in dbBlocks)
            {
                foreach (var variable in db.Variables)
                {
                    var address = variable.GetAddress(db.Number);

                    // 只添加尚未存在的地址
                    if (!_symbolByAddress.ContainsKey(address))
                    {
                        var entry = new SymbolEntry
                        {
                            Name = variable.Name,
                            Address = address,
                            DataType = variable.DataType.ToString(),
                            Comment = variable.Comment
                        };

                        _symbolByName[variable.Name] = entry;
                        _symbolByAddress[address] = entry;
                    }
                }
            }
        }

        _ = SaveAsync();
        SymbolTableChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public int UpdateAddressesFromParsedTags(int dbNumber, List<PlcTag> tags)
    {
        int updatedCount = 0;

        lock (_lock)
        {
            // 查找属于该 DB 的所有符号
            var symbolsToUpdate = _symbolByName.Values
                .Where(s => s.DbNumber == dbNumber && !string.IsNullOrEmpty(s.OriginalSymbolPath))
                .ToList();

            foreach (var entry in symbolsToUpdate)
            {
                // 根据原始符号路径查找新地址
                var tag = tags.FirstOrDefault(t =>
                    t.SymbolicName.Equals(entry.OriginalSymbolPath, StringComparison.OrdinalIgnoreCase));

                if (tag != null && tag.Address != entry.Address)
                {
                    // 更新地址映射
                    _symbolByAddress.Remove(entry.Address);
                    entry.Address = tag.Address;
                    _symbolByAddress[tag.Address] = entry;
                    updatedCount++;

                    System.Diagnostics.Debug.WriteLine($"符号地址更新: {entry.Name} -> {tag.Address}");
                }
            }
        }

        if (updatedCount > 0)
        {
            _ = SaveAsync();
            SymbolTableChanged?.Invoke(this, EventArgs.Empty);
        }

        return updatedCount;
    }

    /// <inheritdoc/>
    public async Task SaveAsync()
    {
        try
        {
            List<SymbolEntry> symbols;
            lock (_lock)
            {
                symbols = _symbolByName.Values.ToList();
            }

            var config = new SymbolConfiguration
            {
                Version = "1.0",
                LastModified = DateTime.Now,
                Symbols = symbols
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(config, options);
            await File.WriteAllTextAsync(_configFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存符号配置失败: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_configFilePath))
                return;

            var json = await File.ReadAllTextAsync(_configFilePath);
            var config = JsonSerializer.Deserialize<SymbolConfiguration>(json);

            if (config?.Symbols != null)
            {
                lock (_lock)
                {
                    _symbolByName.Clear();
                    _symbolByAddress.Clear();

                    foreach (var entry in config.Symbols)
                    {
                        _symbolByName[entry.Name] = entry;
                        _symbolByAddress[entry.Address] = entry;
                    }
                }

                SymbolTableChanged?.Invoke(this, EventArgs.Empty);

                // 自动从 DB 文件刷新地址
                await AutoRefreshFromDbFilesAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载符号配置失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 自动从关联的 DB 文件刷新符号地址
    /// </summary>
    private async Task AutoRefreshFromDbFilesAsync()
    {
        try
        {
            // 获取所有唯一的 DB 文件路径
            Dictionary<string, int> dbFilesToProcess;
            lock (_lock)
            {
                dbFilesToProcess = _symbolByName.Values
                    .Where(s => !string.IsNullOrEmpty(s.DbFilePath) && File.Exists(s.DbFilePath))
                    .GroupBy(s => s.DbFilePath)
                    .ToDictionary(g => g.Key, g => g.First().DbNumber);
            }

            if (dbFilesToProcess.Count == 0) return;

            var parser = new TiaDbParser();
            int totalUpdated = 0;

            foreach (var (filePath, dbNumber) in dbFilesToProcess)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(filePath);
                    var tags = parser.Parse(content, dbNumber);

                    int updated = UpdateAddressesFromParsedTags(dbNumber, tags);
                    totalUpdated += updated;

                    System.Diagnostics.Debug.WriteLine($"从 {Path.GetFileName(filePath)} 更新了 {updated} 个符号地址");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"处理 DB 文件失败 {filePath}: {ex.Message}");
                }
            }

            if (totalUpdated > 0)
            {
                System.Diagnostics.Debug.WriteLine($"自动刷新完成，共更新 {totalUpdated} 个符号地址");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"自动刷新符号地址失败: {ex.Message}");
        }
    }
}
