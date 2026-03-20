using System.IO;
using System.Text.Json;
using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// DB 块管理服务实现
/// </summary>
public class DbBlockService : IDbBlockService
{
    private readonly List<DbBlock> _dbBlocks = new();
    private readonly string _configFileName = "db_blocks_config.json";
    private readonly TiaDbParser _tiaDbParser = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DbBlockService()
    {
        // 异步加载配置
        _ = LoadConfigurationAsync();
    }

    /// <inheritdoc/>
    public List<DbBlock> GetAllDbBlocks() => _dbBlocks.ToList();

    /// <inheritdoc/>
    public void AddDbBlock(DbBlock dbBlock)
    {
        if (_dbBlocks.Any(db => db.Number == dbBlock.Number))
        {
            throw new InvalidOperationException($"DB{dbBlock.Number} 已存在");
        }
        _dbBlocks.Add(dbBlock);
    }

    /// <inheritdoc/>
    public void RemoveDbBlock(int dbNumber)
    {
        var db = _dbBlocks.FirstOrDefault(d => d.Number == dbNumber);
        if (db != null)
        {
            _dbBlocks.Remove(db);
        }
    }

    /// <inheritdoc/>
    public DbBlock? GetDbBlock(int dbNumber)
    {
        return _dbBlocks.FirstOrDefault(db => db.Number == dbNumber);
    }

    /// <inheritdoc/>
    public void UpdateDbBlock(DbBlock dbBlock)
    {
        var index = _dbBlocks.FindIndex(db => db.Number == dbBlock.Number);
        if (index >= 0)
        {
            _dbBlocks[index] = dbBlock;
        }
        else
        {
            _dbBlocks.Add(dbBlock);
        }
    }

    /// <inheritdoc/>
    public void AddVariable(int dbNumber, DbVariable variable)
    {
        var db = GetDbBlock(dbNumber);
        if (db == null)
        {
            throw new InvalidOperationException($"DB{dbNumber} 不存在");
        }

        if (db.Variables.Any(v => v.Name == variable.Name))
        {
            throw new InvalidOperationException($"变量 {variable.Name} 已存在于 DB{dbNumber}");
        }

        db.Variables.Add(variable);
    }

    /// <inheritdoc/>
    public void RemoveVariable(int dbNumber, string variableName)
    {
        var db = GetDbBlock(dbNumber);
        var variable = db?.Variables.FirstOrDefault(v => v.Name == variableName);
        if (variable != null)
        {
            db!.Variables.Remove(variable);
        }
    }

    /// <inheritdoc/>
    public List<(DbBlock Db, DbVariable Variable)> GetSelectedVariables()
    {
        var result = new List<(DbBlock, DbVariable)>();
        foreach (var db in _dbBlocks)
        {
            foreach (var variable in db.Variables.Where(v => v.IsSelected))
            {
                result.Add((db, variable));
            }
        }
        return result;
    }

    /// <inheritdoc/>
    public async Task<string> ExportToJsonAsync(string filePath)
    {
        var config = new DbBlockConfiguration
        {
            Version = "1.0",
            CreatedAt = DateTime.Now,
            ProjectName = "S7MauiApp Export",
            DbBlocks = _dbBlocks
        };

        var json = JsonSerializer.Serialize(config, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
        return filePath;
    }

    /// <inheritdoc/>
    public async Task<bool> ImportFromJsonAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            var json = await File.ReadAllTextAsync(filePath);
            var config = JsonSerializer.Deserialize<DbBlockConfiguration>(json, _jsonOptions);

            if (config?.DbBlocks != null)
            {
                foreach (var db in config.DbBlocks)
                {
                    var existingDb = GetDbBlock(db.Number);
                    if (existingDb != null)
                    {
                        UpdateDbBlock(db);
                    }
                    else
                    {
                        AddDbBlock(db);
                    }
                }
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<DbBlock?> ImportFromDbFileAsync(string filePath, int? dbNumber = null)
    {
        var content = await File.ReadAllTextAsync(filePath);

        // 从文件名或内容中检测 DB 编号
        int detectedDbNumber = VariableParserService.DetectDbNumber(filePath, content);
        int effectiveDbNumber = dbNumber ?? detectedDbNumber;

        // 使用 TiaDbParser 统一解析
        var tags = _tiaDbParser.Parse(content, effectiveDbNumber);
        if (tags == null || tags.Count == 0) return null;

        // 将 PlcTag 转为 DbVariable
        var dbBlock = new DbBlock
        {
            Number = effectiveDbNumber,
            Name = _tiaDbParser.ParsedDbName ?? $"DB{effectiveDbNumber}",
            Description = $"从 {Path.GetFileName(filePath)} 导入",
            Variables = tags
                .Where(t => !t.IsContainer) // 只取叶子变量
                .Select(t => new DbVariable
                {
                    Name = t.SymbolicName,
                    Offset = VariableParserService.ParseByteOffset(t.Address),
                    BitOffset = VariableParserService.ParseBitOffset(t.Address),
                    DataType = MapTagDataType(t.DisplayDataType),
                    Comment = t.Comment,
                    IsSelected = true
                })
                .ToList()
        };

        // 如果没有 DB 编号，自动分配
        if (dbBlock.Number == 0)
        {
            dbBlock.Number = _dbBlocks.Count > 0 ? _dbBlocks.Max(d => d.Number) + 1 : 1;
        }

        // 检查是否存在相同编号的 DB
        var existingDb = GetDbBlock(dbBlock.Number);
        if (existingDb != null)
        {
            UpdateDbBlock(dbBlock);
        }
        else
        {
            AddDbBlock(dbBlock);
        }

        return dbBlock;
    }

    /// <summary>
    /// 将 TiaDbParser 数据类型字符串映射为 PlcDataType 枚举
    /// </summary>
    private static PlcDataType MapTagDataType(string displayDataType)
    {
        return displayDataType.ToLower() switch
        {
            "bool" => PlcDataType.Bool,
            "byte" => PlcDataType.Byte,
            "int" or "word" => PlcDataType.Int,
            "dint" or "dword" or "time" => PlcDataType.DInt,
            "real" => PlcDataType.Real,
            _ => PlcDataType.Int
        };
    }

    /// <inheritdoc/>
    public void SelectAllVariables(int dbNumber, bool selected)
    {
        var db = GetDbBlock(dbNumber);
        if (db != null)
        {
            foreach (var variable in db.Variables)
            {
                variable.IsSelected = selected;
            }
        }
    }

    /// <inheritdoc/>
    public async Task SaveConfigurationAsync()
    {
        try
        {
            var appDataPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "S7WpfApp");
            var filePath = Path.Combine(appDataPath, _configFileName);
            await ExportToJsonAsync(filePath);
        }
        catch
        {
            // 忽略保存错误
        }
    }

    /// <inheritdoc/>
    public async Task LoadConfigurationAsync()
    {
        try
        {
            var appDataPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "S7WpfApp");
            var filePath = Path.Combine(appDataPath, _configFileName);

            if (File.Exists(filePath))
            {
                await ImportFromJsonAsync(filePath);
            }
            else
            {
                // 没有配置文件，添加默认示例 DB
                CreateDefaultDbBlocks();
            }
        }
        catch
        {
            CreateDefaultDbBlocks();
        }
    }

    /// <summary>
    /// 创建默认的示例 DB 块
    /// </summary>
    private void CreateDefaultDbBlocks()
    {
        var db1 = new DbBlock
        {
            Number = 1,
            Name = "HMI_Data",
            Description = "HMI 数据交换块",
            Variables = new List<DbVariable>
            {
                new() { Name = "温度设定值", Offset = 0, DataType = PlcDataType.Real, Comment = "目标温度 (°C)", IsSelected = true },
                new() { Name = "当前温度", Offset = 4, DataType = PlcDataType.Real, Comment = "实际温度 (°C)", IsSelected = true },
                new() { Name = "压力值", Offset = 8, DataType = PlcDataType.Real, Comment = "当前压力 (bar)", IsSelected = true },
                new() { Name = "运行状态", Offset = 12, BitOffset = 0, DataType = PlcDataType.Bool, Comment = "系统运行标志", IsSelected = true },
                new() { Name = "故障状态", Offset = 12, BitOffset = 1, DataType = PlcDataType.Bool, Comment = "故障报警", IsSelected = false },
                new() { Name = "计数器", Offset = 14, DataType = PlcDataType.Int, Comment = "产品计数", IsSelected = true }
            }
        };

        _dbBlocks.Add(db1);
    }

    /// <inheritdoc/>
    public DbBlock CreatePresetDbBlock(string presetName, int dbNumber)
    {
        return presetName.ToLower() switch
        {
            "hmi" => new DbBlock
            {
                Number = dbNumber,
                Name = "HMI_Data",
                Description = "HMI 数据交换块",
                Variables = new List<DbVariable>
                {
                    new() { Name = "Setpoint1", Offset = 0, DataType = PlcDataType.Real, IsSelected = true },
                    new() { Name = "Actual1", Offset = 4, DataType = PlcDataType.Real, IsSelected = true },
                    new() { Name = "Setpoint2", Offset = 8, DataType = PlcDataType.Real, IsSelected = true },
                    new() { Name = "Actual2", Offset = 12, DataType = PlcDataType.Real, IsSelected = true },
                    new() { Name = "RunStatus", Offset = 16, BitOffset = 0, DataType = PlcDataType.Bool, IsSelected = true },
                    new() { Name = "FaultStatus", Offset = 16, BitOffset = 1, DataType = PlcDataType.Bool, IsSelected = true }
                }
            },
            "motor" => new DbBlock
            {
                Number = dbNumber,
                Name = "Motor_Data",
                Description = "电机控制块",
                Variables = new List<DbVariable>
                {
                    new() { Name = "Enable", Offset = 0, BitOffset = 0, DataType = PlcDataType.Bool, IsSelected = true },
                    new() { Name = "Start", Offset = 0, BitOffset = 1, DataType = PlcDataType.Bool, IsSelected = true },
                    new() { Name = "Stop", Offset = 0, BitOffset = 2, DataType = PlcDataType.Bool, IsSelected = true },
                    new() { Name = "Running", Offset = 0, BitOffset = 3, DataType = PlcDataType.Bool, IsSelected = true },
                    new() { Name = "Fault", Offset = 0, BitOffset = 4, DataType = PlcDataType.Bool, IsSelected = true },
                    new() { Name = "Speed", Offset = 2, DataType = PlcDataType.Int, IsSelected = true },
                    new() { Name = "Current", Offset = 4, DataType = PlcDataType.Real, IsSelected = true }
                }
            },
            "pid" => new DbBlock
            {
                Number = dbNumber,
                Name = "PID_Data",
                Description = "PID 控制块",
                Variables = new List<DbVariable>
                {
                    new() { Name = "Setpoint", Offset = 0, DataType = PlcDataType.Real, IsSelected = true },
                    new() { Name = "ProcessValue", Offset = 4, DataType = PlcDataType.Real, IsSelected = true },
                    new() { Name = "Output", Offset = 8, DataType = PlcDataType.Real, IsSelected = true },
                    new() { Name = "Kp", Offset = 12, DataType = PlcDataType.Real, IsSelected = true },
                    new() { Name = "Ki", Offset = 16, DataType = PlcDataType.Real, IsSelected = true },
                    new() { Name = "Kd", Offset = 20, DataType = PlcDataType.Real, IsSelected = true },
                    new() { Name = "ManualMode", Offset = 24, BitOffset = 0, DataType = PlcDataType.Bool, IsSelected = true },
                    new() { Name = "ManualOutput", Offset = 26, DataType = PlcDataType.Real, IsSelected = true }
                }
            },
            _ => new DbBlock
            {
                Number = dbNumber,
                Name = $"DB{dbNumber}",
                Description = "自定义数据块"
            }
        };
    }
}

