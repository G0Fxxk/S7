using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// PLC 通信服务接口
/// </summary>
public interface IPlcService
{
    /// <summary>
    /// 获取当前连接状态
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    event EventHandler<bool>? ConnectionStatusChanged;

    /// <summary>
    /// 获取当前生效的连接配置（供 Sharp7 采样引擎使用）
    /// </summary>
    PlcConnectionConfig? GetCurrentConfig();

    /// <summary>
    /// 延迟更新事件 (ms)
    /// </summary>
    event EventHandler<long>? LatencyUpdated;

    /// <summary>
    /// PLC CPU 状态变化事件 ("Run" / "Stop" / "Unknown")
    /// </summary>
    event EventHandler<string>? PlcCpuStatusChanged;

    /// <summary>
    /// 当前 PLC CPU 状态 ("Run" / "Stop" / "Unknown")
    /// </summary>
    string PlcCpuStatus { get; }

    /// <summary>
    /// 错误发生事件
    /// </summary>
    event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// 日志消息事件 (用于调试)
    /// </summary>
    event EventHandler<string>? LogMessage;

    /// <summary>
    /// 请求暂停/恢复其他模块的周期刷新（true=暂停, false=恢复）
    /// 趋势采样开始时触发暂停，停止时触发恢复
    /// </summary>
    event EventHandler<bool>? RefreshPauseRequested;

    /// <summary>
    /// 请求暂停其他模块刷新（趋势采样独占通信）
    /// </summary>
    void RequestPauseRefresh();

    /// <summary>
    /// 请求恢复其他模块刷新
    /// </summary>
    void RequestResumeRefresh();

    /// <summary>
    /// 当前延迟 (ms)
    /// </summary>
    long PingLatency { get; }

    /// <summary>
    /// 是否启用自动重连
    /// </summary>
    bool AutoReconnect { get; set; }

    /// <summary>
    /// 连接到 PLC
    /// </summary>
    Task<bool> ConnectAsync(PlcConnectionConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开与 PLC 的连接
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取数据
    /// </summary>
    Task<T?> ReadAsync<T>(string address, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据地址格式自动读取数据 (DBX=Bool, DBB=Byte, DBW=Int, DBD=Real)
    /// </summary>
    Task<object?> ReadAutoAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取字节数组 (用于 Array 和复杂类型)
    /// </summary>
    /// <param name="dbNumber">DB 编号</param>
    /// <param name="startByte">起始字节地址</param>
    /// <param name="count">读取字节数</param>
    Task<byte[]> ReadBytesAsync(int dbNumber, int startByte, int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取字符串 (S7 String 类型)
    /// </summary>
    /// <param name="dbNumber">DB 编号</param>
    /// <param name="startByte">起始字节地址</param>
    /// <param name="maxLength">最大长度</param>
    Task<string> ReadStringAsync(int dbNumber, int startByte, int maxLength = 254, CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入数据
    /// </summary>
    Task WriteAsync<T>(string address, T value, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据地址格式自动写入数据 (DBX=Bool, DBB=Byte, DBW=Int, DBD=Real)
    /// </summary>
    Task WriteAutoAsync(string address, object value, CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入字节数组
    /// </summary>
    Task WriteBytesAsync(int dbNumber, int startByte, byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入字符串 (S7 String 类型)
    /// </summary>
    Task WriteStringAsync(int dbNumber, int startByte, string value, int maxLength = 254, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量读取多个数据项
    /// </summary>
    Task<List<PlcDataItem>> ReadMultipleAsync(List<PlcDataItem> items, CancellationToken cancellationToken = default);
}

