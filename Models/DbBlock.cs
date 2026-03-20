using System.Text.Json.Serialization;

namespace S7WpfApp.Models;

/// <summary>
/// DB 块定义模型 - 表示一个完整的 PLC 数据块
/// </summary>
public class DbBlock
{
    /// <summary>
    /// DB 块编号（如 1 表示 DB1）
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// DB 块名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// DB 块描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// DB 块中的变量列表
    /// </summary>
    public List<DbVariable> Variables { get; set; } = new();

    /// <summary>
    /// 获取格式化的 DB 名称
    /// </summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrEmpty(Name) ? $"DB{Number}" : $"DB{Number} - {Name}";

    /// <summary>
    /// 计算 DB 块的总字节大小
    /// </summary>
    [JsonIgnore]
    public int TotalSize => Variables.Sum(v => v.GetByteSize());
}

/// <summary>
/// DB 块中的变量定义
/// </summary>
public class DbVariable
{
    /// <summary>
    /// 变量名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 变量在 DB 中的偏移量（字节）
    /// </summary>
    public int Offset { get; set; }

    /// <summary>
    /// 位偏移（仅对 Bool 类型有效，0-7）
    /// </summary>
    public int BitOffset { get; set; }

    /// <summary>
    /// 数据类型
    /// </summary>
    public PlcDataType DataType { get; set; }

    /// <summary>
    /// 变量描述/注释
    /// </summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>
    /// 是否被选中用于监控
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// 当前值（运行时使用）
    /// </summary>
    [JsonIgnore]
    public object? CurrentValue { get; set; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    [JsonIgnore]
    public DateTime LastUpdateTime { get; set; }

    /// <summary>
    /// 获取完整的 S7 地址
    /// </summary>
    public string GetAddress(int dbNumber)
    {
        return DataType switch
        {
            PlcDataType.Bool => $"DB{dbNumber}.DBX{Offset}.{BitOffset}",
            PlcDataType.Byte => $"DB{dbNumber}.DBB{Offset}",
            PlcDataType.Int => $"DB{dbNumber}.DBW{Offset}",
            PlcDataType.DInt => $"DB{dbNumber}.DBD{Offset}",
            PlcDataType.Real => $"DB{dbNumber}.DBD{Offset}",
            PlcDataType.String => $"DB{dbNumber}.DBB{Offset}",
            _ => $"DB{dbNumber}.DBB{Offset}"
        };
    }

    /// <summary>
    /// 获取数据类型的字节大小
    /// </summary>
    public int GetByteSize()
    {
        return DataType switch
        {
            PlcDataType.Bool => 1,  // Bool 占用 1 位，但地址以字节为单位
            PlcDataType.Byte => 1,
            PlcDataType.Int => 2,
            PlcDataType.DInt => 4,
            PlcDataType.Real => 4,
            PlcDataType.String => 256,  // 默认字符串长度
            _ => 1
        };
    }

    /// <summary>
    /// 格式化显示当前值
    /// </summary>
    [JsonIgnore]
    public string FormattedValue => CurrentValue?.ToString() ?? "N/A";
}

/// <summary>
/// DB 块配置文件模型（用于导入/导出）
/// </summary>
public class DbBlockConfiguration
{
    /// <summary>
    /// 配置版本
    /// </summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// PLC 项目名称
    /// </summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    /// DB 块列表
    /// </summary>
    public List<DbBlock> DbBlocks { get; set; } = new();
}
