namespace S7WpfApp.Models;

/// <summary>
/// PLC 数据项类型枚举
/// </summary>
public enum PlcDataType
{
    Bool,
    Byte,
    Int,      // S7 INT (16-bit)
    DInt,     // S7 DINT (32-bit)
    Real,     // S7 REAL (32-bit float)
    String
}

/// <summary>
/// PLC 内存区域枚举
/// </summary>
public enum PlcMemoryArea
{
    Input,      // I - 输入区
    Output,     // Q - 输出区
    Marker,     // M - 标志区
    DataBlock,  // DB - 数据块
    Timer,      // T - 定时器
    Counter     // C - 计数器
}

/// <summary>
/// PLC 数据项模型 - 用于表示和管理 PLC 中的单个变量
/// </summary>
public class PlcDataItem
{
    /// <summary>
    /// 变量唯一标识符
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 变量名称（用户定义的友好名称）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// PLC 地址（如 "DB1.DBD0", "M0.0", "I0.0"）
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// 数据类型
    /// </summary>
    public PlcDataType DataType { get; set; } = PlcDataType.Bool;

    /// <summary>
    /// 当前值
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// 是否可写
    /// </summary>
    public bool IsWritable { get; set; } = true;

    /// <summary>
    /// 变量描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdateTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 格式化后的值字符串
    /// </summary>
    public string FormattedValue => Value?.ToString() ?? "N/A";
}
