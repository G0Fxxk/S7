namespace S7WpfApp.Models;

/// <summary>
/// 趋势数据点
/// </summary>
public class TrendDataPoint
{
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 变量名
    /// </summary>
    public string VariableName { get; set; } = "";

    /// <summary>
    /// 数值
    /// </summary>
    public double Value { get; set; }
}

/// <summary>
/// 监控变量配置
/// </summary>
public class TrendVariable
{
    /// <summary>
    /// 变量名
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 显示名称
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// 数据类型
    /// </summary>
    public string DataType { get; set; } = "";

    /// <summary>
    /// DB 编号
    /// </summary>
    public int DbNumber { get; set; }

    /// <summary>
    /// 字节偏移
    /// </summary>
    public int ByteOffset { get; set; }

    /// <summary>
    /// 位偏移
    /// </summary>
    public int BitOffset { get; set; }

    /// <summary>
    /// 大小
    /// </summary>
    public int Size { get; set; }

    /// <summary>
    /// 当前值
    /// </summary>
    public double CurrentValue { get; set; }

    /// <summary>
    /// 是否选中监控
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// 启用报警
    /// </summary>
    public bool AlarmEnabled { get; set; }

    /// <summary>
    /// 报警下限
    /// </summary>
    public double AlarmLowLimit { get; set; } = double.MinValue;

    /// <summary>
    /// 报警上限
    /// </summary>
    public double AlarmHighLimit { get; set; } = double.MaxValue;

    /// <summary>
    /// 是否报警状态
    /// </summary>
    public bool IsInAlarm => AlarmEnabled && (CurrentValue < AlarmLowLimit || CurrentValue > AlarmHighLimit);

    /// <summary>
    /// S7 地址
    /// </summary>
    public string S7Address { get; set; } = "";
}
