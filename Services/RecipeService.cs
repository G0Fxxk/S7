using System.IO;
using System.Text;
using System.Text.Json;
using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// 配方服务实现 - JSON 持久化 + PLC 读写 + CSV 导入导出
/// </summary>
public class RecipeService : IRecipeService
{
    private readonly string _configFilePath;
    private readonly IPlcService _plcService;
    private readonly ISymbolService _symbolService;
    private readonly IRecipeLogService _logService;
    private List<RecipeTable> _tables = new();
    private readonly object _lock = new();

    public RecipeService(IPlcService plcService, ISymbolService symbolService, IRecipeLogService logService)
    {
        _plcService = plcService;
        _symbolService = symbolService;
        _logService = logService;

        _configFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "S7WpfApp", "recipes.json");

        LoadFromFile();
    }

    #region 持久化

    private void LoadFromFile()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                var config = JsonSerializer.Deserialize<RecipeConfiguration>(json);
                if (config?.Tables != null)
                    _tables = config.Tables;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载配方配置失败: {ex.Message}");
            _tables = new();
        }
    }

    public void SaveAll()
    {
        lock (_lock)
        {
            try
            {
                var config = new RecipeConfiguration { Tables = _tables };
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存配方配置失败: {ex.Message}");
            }
        }
    }

    #endregion

    #region 配方表 CRUD

    public List<RecipeTable> GetTables()
    {
        lock (_lock) { return _tables.ToList(); }
    }

    public void SaveTable(RecipeTable table)
    {
        lock (_lock)
        {
            var existing = _tables.FirstOrDefault(t => t.Id == table.Id);
            if (existing != null)
            {
                var idx = _tables.IndexOf(existing);
                _tables[idx] = table;
            }
            else
            {
                _tables.Add(table);
            }
            SaveAll();
        }
        _logService.Log("保存", table.Name, detail: $"{table.Variables.Count} 个变量, {table.DataSets.Count} 个数据集");
    }

    public void DeleteTable(string tableId)
    {
        lock (_lock)
        {
            var table = _tables.FirstOrDefault(t => t.Id == tableId);
            if (table != null)
            {
                _tables.Remove(table);
                SaveAll();
                _logService.Log("删除", table.Name);
            }
        }
    }

    #endregion

    #region 数据集管理

    public RecipeDataSet AddDataSet(RecipeTable table, string name)
    {
        var dataSet = new RecipeDataSet { Name = name };
        // 初始化所有变量的默认值
        foreach (var v in table.Variables)
        {
            dataSet.Values[v.Name] = GetDefaultValue(v.DataType);
        }
        table.DataSets.Add(dataSet);
        SaveAll();
        _logService.Log("新建数据集", table.Name, name);
        return dataSet;
    }

    public void DeleteDataSet(RecipeTable table, string dataSetId)
    {
        var ds = table.DataSets.FirstOrDefault(d => d.Id == dataSetId);
        if (ds != null)
        {
            table.DataSets.Remove(ds);
            SaveAll();
            _logService.Log("删除数据集", table.Name, ds.Name);
        }
    }

    private static string GetDefaultValue(string dataType) => dataType.ToLower() switch
    {
        "bool" => "False",
        "real" => "0.0",
        _ => "0"
    };

    #endregion

    #region PLC 读写

    public async Task ReadFromPlcAsync(RecipeTable table, RecipeDataSet dataSet)
    {
        if (!_plcService.IsConnected)
            throw new InvalidOperationException("PLC 未连接");

        // 先解析符号地址
        ResolveSymbolAddresses(table);

        var errors = new List<string>();
        foreach (var varDef in table.Variables)
        {
            if (string.IsNullOrEmpty(varDef.Address))
            {
                errors.Add($"{varDef.Name}: 地址为空");
                continue;
            }

            try
            {
                var value = await ReadTypedValueAsync(varDef.Address, varDef.DataType);
                dataSet.Values[varDef.Name] = value;
            }
            catch (Exception ex)
            {
                errors.Add($"{varDef.Name}: {ex.Message}");
            }
        }

        dataSet.ModifiedAt = DateTime.Now;
        SaveAll();

        var detail = errors.Count > 0
            ? $"读取 {table.Variables.Count - errors.Count}/{table.Variables.Count} 成功, 失败: {string.Join("; ", errors)}"
            : $"读取 {table.Variables.Count} 个变量成功";
        _logService.Log("从PLC读取", table.Name, dataSet.Name, detail);

        if (errors.Count > 0)
            throw new Exception($"部分变量读取失败:\n{string.Join("\n", errors)}");
    }

    public async Task WriteToPlcAsync(RecipeTable table, RecipeDataSet dataSet)
    {
        if (!_plcService.IsConnected)
            throw new InvalidOperationException("PLC 未连接");

        // 先解析符号地址
        ResolveSymbolAddresses(table);

        var errors = new List<string>();
        foreach (var varDef in table.Variables)
        {
            if (string.IsNullOrEmpty(varDef.Address))
            {
                errors.Add($"{varDef.Name}: 地址为空");
                continue;
            }

            if (!dataSet.Values.TryGetValue(varDef.Name, out var valueStr) || string.IsNullOrEmpty(valueStr))
                continue;

            try
            {
                await WriteTypedValueAsync(varDef.Address, varDef.DataType, valueStr);
            }
            catch (Exception ex)
            {
                errors.Add($"{varDef.Name}: {ex.Message}");
            }
        }

        var detail = errors.Count > 0
            ? $"写入 {table.Variables.Count - errors.Count}/{table.Variables.Count} 成功, 失败: {string.Join("; ", errors)}"
            : $"写入 {table.Variables.Count} 个变量成功";
        _logService.Log("写入PLC", table.Name, dataSet.Name, detail);

        if (errors.Count > 0)
            throw new Exception($"部分变量写入失败:\n{string.Join("\n", errors)}");
    }

    private async Task<string> ReadTypedValueAsync(string address, string dataType)
    {
        return dataType.ToLower() switch
        {
            "bool" => (await _plcService.ReadAsync<bool>(address)).ToString(),
            "byte" => (await _plcService.ReadAsync<byte>(address)).ToString(),
            "char" => ((char)(await _plcService.ReadAsync<byte>(address))).ToString(),
            "int" => (await _plcService.ReadAsync<short>(address)).ToString(),
            "dint" => (await _plcService.ReadAsync<int>(address)).ToString(),
            "real" => (await _plcService.ReadAsync<float>(address)).ToString("F3"),
            _ => (await _plcService.ReadAutoAsync(address))?.ToString() ?? ""
        };
    }

    private async Task WriteTypedValueAsync(string address, string dataType, string valueStr)
    {
        switch (dataType.ToLower())
        {
            case "bool":
                await _plcService.WriteAsync(address, bool.Parse(valueStr));
                break;
            case "byte":
                await _plcService.WriteAsync(address, byte.Parse(valueStr));
                break;
            case "char":
                byte charByte = string.IsNullOrEmpty(valueStr) ? (byte)0 : (byte)valueStr[0];
                await _plcService.WriteAsync(address, charByte);
                break;
            case "int":
                await _plcService.WriteAsync(address, short.Parse(valueStr));
                break;
            case "dint":
                await _plcService.WriteAsync(address, int.Parse(valueStr));
                break;
            case "real":
                await _plcService.WriteAsync(address, float.Parse(valueStr));
                break;
            default:
                await _plcService.WriteAutoAsync(address, valueStr);
                break;
        }
    }

    #endregion

    #region CSV 导入/导出

    public async Task ExportToCsvAsync(RecipeTable table, string filePath)
    {
        var sb = new StringBuilder();

        // 表头行：变量名, 地址, 符号名, 类型, 备注, [数据集1], [数据集2], ...
        var header = new List<string> { "变量名", "地址", "符号名", "数据类型", "备注" };
        foreach (var ds in table.DataSets)
            header.Add(ds.Name);
        sb.AppendLine(string.Join(",", header.Select(EscapeCsv)));

        // 数据行
        foreach (var varDef in table.Variables)
        {
            var row = new List<string>
            {
                varDef.Name,
                varDef.Address,
                varDef.SymbolName,
                varDef.DataType,
                varDef.Comment
            };
            foreach (var ds in table.DataSets)
            {
                row.Add(ds.Values.TryGetValue(varDef.Name, out var val) ? val : "");
            }
            sb.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
        _logService.Log("导出CSV", table.Name, detail: $"导出到 {filePath}");
    }

    public async Task<RecipeTable> ImportFromCsvAsync(string filePath)
    {
        var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);
        if (lines.Length < 2)
            throw new Exception("CSV 文件至少需要 2 行（表头 + 数据）");

        var headerCells = ParseCsvLine(lines[0]);
        if (headerCells.Count < 5)
            throw new Exception("CSV 格式错误：表头至少需要 5 列（变量名, 地址, 符号名, 数据类型, 备注）");

        // 提取数据集名称（第 6 列起）
        var dataSetNames = headerCells.Skip(5).ToList();

        var table = new RecipeTable
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            Variables = new(),
            DataSets = dataSetNames.Select(n => new RecipeDataSet { Name = n }).ToList()
        };

        // 解析数据行
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cells = ParseCsvLine(lines[i]);
            if (cells.Count < 5) continue;

            var varDef = new RecipeVariableDefinition
            {
                Name = cells[0],
                Address = cells[1],
                SymbolName = cells[2],
                DataType = cells[3],
                Comment = cells[4]
            };
            table.Variables.Add(varDef);

            // 填充各数据集的值
            for (int j = 0; j < dataSetNames.Count && j + 5 < cells.Count; j++)
            {
                table.DataSets[j].Values[varDef.Name] = cells[j + 5];
            }
        }

        // 保存到配方列表
        lock (_lock)
        {
            _tables.Add(table);
            SaveAll();
        }

        _logService.Log("导入CSV", table.Name, detail: $"从 {filePath} 导入 {table.Variables.Count} 个变量, {table.DataSets.Count} 个数据集");
        return table;
    }

    /// <summary>
    /// CSV 字段转义
    /// </summary>
    private static string EscapeCsv(string field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }

    /// <summary>
    /// 解析 CSV 行（支持引号内的逗号）
    /// </summary>
    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++; // 跳过转义的引号
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result;
    }

    #endregion

    #region 符号解析

    public void ResolveSymbolAddresses(RecipeTable table)
    {
        foreach (var varDef in table.Variables)
        {
            if (!string.IsNullOrEmpty(varDef.SymbolName))
            {
                var address = _symbolService.GetAddress(varDef.SymbolName);
                if (!string.IsNullOrEmpty(address))
                {
                    varDef.Address = address;
                }
            }
        }
    }

    #endregion
}
