namespace S7WpfApp.Models;

/// <summary>
/// PLC 连接配置模型
/// </summary>
public class PlcConnectionConfig
{
    /// <summary>
    /// PLC IP 地址
    /// </summary>
    public string IpAddress { get; set; } = "192.168.1.200";

    /// <summary>
    /// 机架号 (通常为 0)
    /// </summary>
    public short Rack { get; set; } = 0;

    /// <summary>
    /// 插槽号 (S7-1200 通常为 1)
    /// </summary>
    public short Slot { get; set; } = 1;

    /// <summary>
    /// CPU 类型
    /// </summary>
    public S7CpuType CpuType { get; set; } = S7CpuType.S71200;

    /// <summary>
    /// 连接超时时间（毫秒）
    /// </summary>
    public int ConnectionTimeout { get; set; } = 5000;

    /// <summary>
    /// 读取超时时间（毫秒）
    /// </summary>
    public int ReadTimeout { get; set; } = 1000;

    /// <summary>
    /// 写入超时时间（毫秒）
    /// </summary>
    public int WriteTimeout { get; set; } = 1000;
}

/// <summary>
/// S7 CPU 类型枚举
/// </summary>
public enum S7CpuType
{
    S7200 = 0,
    S7200Smart = 1,
    S7300 = 10,
    S7400 = 20,
    S71200 = 30,
    S71500 = 40
}
