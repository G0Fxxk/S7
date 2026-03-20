using S7.Net;
using S7WpfApp.Models;
using Microsoft.Extensions.Logging;

namespace S7WpfApp.Services;

/// <summary>
/// PLC 通信服务实现 - 使用 S7.net 库
/// </summary>
public class PlcService : IPlcService, IDisposable
{
    private readonly ILogger<PlcService> _logger;
    private Plc? _plc;
    private bool _isConnected;
    private readonly SemaphoreSlim _plcSemaphore = new(1, 1); // 保护 _plc 并发访问
    private System.Timers.Timer? _heartbeatTimer;
    private PlcConnectionConfig? _lastConfig;
    private bool _isManuallyDisconnected;
    private int _reconnectAttempts = 0;
    private const int MAX_RECONNECT_ATTEMPTS = 5;
    private bool? _lastHeartbeatConnected; // 追踪心跳连接状态变化，仅在变化时记录日志
    private const int PING_TIMEOUT_MS = 500;       // Ping 预检超时（毫秒）
    private const int OPEN_TIMEOUT_MS = 5000;      // Open 操作超时（毫秒）
    private const int CLOSE_TIMEOUT_MS = 3000;     // Close 操作超时（毫秒）

    // Sharp7 CPU 状态查询：复用持久连接，降低查询频率
    private Sharp7.S7Client? _sharp7Client;
    private int _cpuStatusCheckCounter = 0;
    private const int CPU_STATUS_CHECK_INTERVAL = 5; // 每 5 次心跳查询一次 CPU 状态

    public PlcService(ILogger<PlcService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public long PingLatency { get; private set; }

    /// <inheritdoc/>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// 当前 PLC CPU 状态
    /// </summary>
    private string _plcCpuStatus = "Unknown";
    public string PlcCpuStatus
    {
        get => _plcCpuStatus;
        private set
        {
            if (_plcCpuStatus != value)
            {
                _plcCpuStatus = value;
                PlcCpuStatusChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<string>? PlcCpuStatusChanged;

    /// <inheritdoc/>
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                ConnectionStatusChanged?.Invoke(this, value);
            }
        }
    }

    /// <inheritdoc/>
    public event EventHandler<bool>? ConnectionStatusChanged;

    public PlcConnectionConfig? GetCurrentConfig() => _lastConfig;

    /// <inheritdoc/>
    public event EventHandler<long>? LatencyUpdated;

    /// <inheritdoc/>
    public event EventHandler<string>? ErrorOccurred;

    /// <inheritdoc/>
    public event EventHandler<string>? LogMessage;

    /// <inheritdoc/>
    public event EventHandler<bool>? RefreshPauseRequested;

    /// <inheritdoc/>
    public void RequestPauseRefresh() => RefreshPauseRequested?.Invoke(this, true);

    /// <inheritdoc/>
    public void RequestResumeRefresh() => RefreshPauseRequested?.Invoke(this, false);

    private void Log(string message)
    {
        _logger.LogDebug(message);
        LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    // ═══════════════ 辅助方法：Ping 预检 / 超时保护 / CpuType 映射 ═══════════════

    /// <summary>
    /// Ping 预检：用短超时 ICMP Ping 快速判断 PLC 是否在线，
    /// 避免直接调用 Open() 时陷入 TCP 21 秒超时黑洞
    /// </summary>
    private async Task<bool> PingCheckAsync(string ip, int timeoutMs = PING_TIMEOUT_MS)
    {
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = await ping.SendPingAsync(ip, timeoutMs);
            return reply.Status == System.Net.NetworkInformation.IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 带超时保护的 Open 操作，防止 TCP 黑洞导致长时间阻塞
    /// </summary>
    private async Task<bool> OpenWithTimeoutAsync(Plc plc, int timeoutMs = OPEN_TIMEOUT_MS)
    {
        var openTask = Task.Run(() => plc.Open());
        var completedTask = await Task.WhenAny(openTask, Task.Delay(timeoutMs));

        if (completedTask == openTask)
        {
            await openTask; // 传播可能的异常
            return plc.IsConnected;
        }

        // 超时：后台线程的 Open 可能还在阻塞，但主动返回失败
        Log($"OpenWithTimeoutAsync: Open 操作超时 ({timeoutMs}ms)，放弃等待");
        return false;
    }

    /// <summary>
    /// 带超时保护的 Close 操作，防止断线场景下 Close 同样阻塞
    /// </summary>
    private async Task CloseWithTimeoutAsync(Plc plc, int timeoutMs = CLOSE_TIMEOUT_MS)
    {
        var closeTask = Task.Run(() => plc.Close());
        if (await Task.WhenAny(closeTask, Task.Delay(timeoutMs)) != closeTask)
        {
            Log($"CloseWithTimeoutAsync: Close 操作超时 ({timeoutMs}ms)，放弃等待");
        }
    }

    /// <summary>
    /// 将自定义 S7CpuType 映射到 S7.Net 的 CpuType（消除重复代码）
    /// </summary>
    private static CpuType MapCpuType(S7CpuType cpuType) => cpuType switch
    {
        S7CpuType.S7200 => CpuType.S7200,
        S7CpuType.S7200Smart => CpuType.S7200Smart,
        S7CpuType.S7300 => CpuType.S7300,
        S7CpuType.S7400 => CpuType.S7400,
        S7CpuType.S71200 => CpuType.S71200,
        S7CpuType.S71500 => CpuType.S71500,
        _ => CpuType.S71200
    };

    /// <inheritdoc/>
    public async Task<bool> ConnectAsync(PlcConnectionConfig config, CancellationToken cancellationToken = default)
    {
        await _plcSemaphore.WaitAsync(cancellationToken);
        try
        {
            Log($"ConnectAsync: 开始连接 IP={config.IpAddress}, Rack={config.Rack}, Slot={config.Slot}");
            Log($"ConnectAsync: _isManuallyDisconnected={_isManuallyDisconnected}, AutoReconnect={AutoReconnect}");

            // Reset manual disconnect flag at start of connection attempt
            _isManuallyDisconnected = false;

            await CloseConnectionInternalAsync();

            // Ping 预检：快速判断目标是否在线，避免 TCP 21 秒黑洞
            if (!await PingCheckAsync(config.IpAddress))
            {
                Log("ConnectAsync: Ping 预检失败，目标不可达");
                OnError("连接失败: PLC 不可达（Ping 超时）");
                IsConnected = false;
                return false;
            }

            _plc = new Plc(MapCpuType(config.CpuType), config.IpAddress, config.Rack, config.Slot);
            _plc.ReadTimeout = config.ReadTimeout;
            _plc.WriteTimeout = config.WriteTimeout;

            Log("ConnectAsync: Ping 预检通过，正在打开 S7 连接...");
            var opened = await OpenWithTimeoutAsync(_plc, config.ConnectionTimeout);
            if (!opened)
            {
                Log("ConnectAsync: Open 超时或失败");
                OnError("连接失败: S7 连接超时");
                IsConnected = false;
                return false;
            }

            IsConnected = _plc.IsConnected;
            Log($"ConnectAsync: 连接结果 IsConnected={IsConnected}");

            if (IsConnected)
            {
                _lastConfig = config;
                // _isManuallyDisconnected is already false
                StartHeartbeat();
                Log("ConnectAsync: 心跳已启动");
            }

            return IsConnected;
        }
        catch (Exception ex)
        {
            Log($"ConnectAsync: 连接异常 - {ex.Message}");
            OnError($"连接失败: {ex.Message}");
            IsConnected = false;
            return false;
        }
        finally
        {
            _plcSemaphore.Release();
        }
    }

    /// <summary>
    /// 内部重连方法 - 不影响心跳定时器
    /// </summary>
    private async Task<bool> TryReconnectInternalAsync(PlcConnectionConfig config)
    {
        await _plcSemaphore.WaitAsync();
        try
        {
            Log($"TryReconnectInternal: 开始内部重连 IP={config.IpAddress}");

            // 关闭旧连接但不停止心跳（带超时保护）
            if (_plc != null)
            {
                try
                {
                    await CloseWithTimeoutAsync(_plc);
                    _plc = null;
                    Log("TryReconnectInternal: 旧连接已关闭");
                }
                catch (Exception closeEx)
                {
                    Log($"TryReconnectInternal: 关闭旧连接异常(忽略) - {closeEx.Message}");
                    _plc = null;
                }
            }

            // Ping 预检：快速判断目标是否在线
            if (!await PingCheckAsync(config.IpAddress))
            {
                Log("TryReconnectInternal: Ping 预检失败，跳过本次重连");
                IsConnected = false;
                return false;
            }

            _plc = new Plc(MapCpuType(config.CpuType), config.IpAddress, config.Rack, config.Slot);
            _plc.ReadTimeout = config.ReadTimeout;
            _plc.WriteTimeout = config.WriteTimeout;

            Log("TryReconnectInternal: Ping 预检通过，正在打开 S7 连接...");
            var opened = await OpenWithTimeoutAsync(_plc, config.ConnectionTimeout);
            if (!opened)
            {
                Log("TryReconnectInternal: Open 超时或失败");
                IsConnected = false;
                return false;
            }

            IsConnected = _plc.IsConnected;
            Log($"TryReconnectInternal: 连接结果 IsConnected={IsConnected}");

            if (IsConnected)
            {
                _lastConfig = config;
            }

            return IsConnected;
        }
        catch (Exception ex)
        {
            Log($"TryReconnectInternal: 重连异常 - {ex.Message}");
            OnError($"连接失败: {ex.Message}");
            IsConnected = false;
            return false;
        }
        finally
        {
            _plcSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        Log("DisconnectAsync: 用户主动断开连接");
        _isManuallyDisconnected = true;
        await CloseConnectionAsync();
    }

    /// <summary>
    /// 公共关闭方法 — 获取信号量后调用内部实现
    /// </summary>
    private async Task CloseConnectionAsync()
    {
        await _plcSemaphore.WaitAsync();
        try
        {
            await CloseConnectionInternalAsync();
        }
        finally
        {
            _plcSemaphore.Release();
        }
    }

    /// <summary>
    /// 内部关闭实现 — 调用方必须已持有 _plcSemaphore
    /// </summary>
    private async Task CloseConnectionInternalAsync()
    {
        try
        {
            if (_plc != null)
            {
                Log("CloseConnectionAsync: 正在关闭连接...");
                // Stop heartbeat if it's running, to avoid race conditions during close
                StopHeartbeat();
                await CloseWithTimeoutAsync(_plc);
                _plc = null;
                Log("CloseConnectionAsync: 连接已关闭");
            }
            IsConnected = false;
        }
        catch (Exception ex)
        {
            Log($"CloseConnectionAsync: 关闭异常 - {ex.Message}");
            OnError($"关闭连接失败: {ex.Message}");
            _plc = null; // 异常时也要清理引用，防止残留
            IsConnected = false;
        }
    }

    /// <inheritdoc/>
    public async Task<T?> ReadAsync<T>(string address, CancellationToken cancellationToken = default)
    {
        await _plcSemaphore.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();

            var result = await Task.Run(() => _plc!.Read(address));
            if (result == null) return default;

            if (typeof(T) == typeof(float))
            {
                if (result is uint uintValue)
                {
                    var bytes = BitConverter.GetBytes(uintValue);
                    return (T)(object)BitConverter.ToSingle(bytes, 0);
                }
                else if (result is float floatValue)
                {
                    return (T)(object)floatValue;
                }
                else if (result is byte[] byteArray && byteArray.Length >= 4)
                {
                    return (T)(object)BitConverter.ToSingle(byteArray, 0);
                }
            }

            return (T?)Convert.ChangeType(result, typeof(T));
        }
        catch (Exception ex)
        {
            OnError($"读取 {address} 失败: {ex.Message}");
            return default;
        }
        finally
        {
            _plcSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<object?> ReadAutoAsync(string address, CancellationToken cancellationToken = default)
    {
        // 注意：对于已有 Semaphore 保护的 ReadAsync，此处不再重复加锁
        // 仅对 else 分支（直接读取）需要加保护
        var upperAddr = address.ToUpperInvariant();

        try
        {
            if (upperAddr.Contains(".DBX"))
            {
                return await ReadAsync<bool>(address);
            }
            else if (upperAddr.Contains(".DBB"))
            {
                return await ReadAsync<byte>(address);
            }
            else if (upperAddr.Contains(".DBW"))
            {
                return await ReadAsync<short>(address);
            }
            else if (upperAddr.Contains(".DBD"))
            {
                return await ReadAsync<float>(address);
            }
            else
            {
                // 默认直接读取 — 需要加锁
                await _plcSemaphore.WaitAsync();
                try
                {
                    EnsureConnected();
                    var result = await Task.Run(() => _plc!.Read(address));
                    return result;
                }
                finally
                {
                    _plcSemaphore.Release();
                }
            }
        }
        catch (Exception ex)
        {
            OnError($"自动读取 {address} 失败: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task WriteAsync<T>(string address, T value, CancellationToken cancellationToken = default)
    {
        await _plcSemaphore.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();

            if (value == null) throw new ArgumentNullException(nameof(value));

            if (value is float floatVal)
            {
                var bytes = BitConverter.GetBytes(floatVal);
                var uintValue = BitConverter.ToUInt32(bytes, 0);
                await Task.Run(() => _plc!.Write(address, uintValue));
                return;
            }

            await Task.Run(() => _plc!.Write(address, (object)value));
        }
        catch (Exception ex)
        {
            OnError($"写入 {address} 失败: {ex.Message}");
            throw;
        }
        finally
        {
            _plcSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task WriteAutoAsync(string address, object value, CancellationToken cancellationToken = default)
    {
        // WriteAsync 已有 Semaphore 保护，此处不再重复加锁
        var upperAddr = address.ToUpperInvariant();

        try
        {
            if (upperAddr.Contains(".DBX"))
            {
                await WriteAsync(address, Convert.ToBoolean(value));
            }
            else if (upperAddr.Contains(".DBB"))
            {
                await WriteAsync(address, Convert.ToByte(value));
            }
            else if (upperAddr.Contains(".DBW"))
            {
                await WriteAsync(address, Convert.ToInt16(value));
            }
            else if (upperAddr.Contains(".DBD"))
            {
                await WriteAsync(address, Convert.ToSingle(value));
            }
            else
            {
                // 默认直接写入
                await WriteAsync(address, value);
            }
        }
        catch (Exception ex)
        {
            OnError($"自动写入 {address} 失败: {ex.Message}");
            throw;
        }
    }


    /// <inheritdoc/>
    public async Task<byte[]> ReadBytesAsync(int dbNumber, int startByte, int count, CancellationToken cancellationToken = default)
    {
        await _plcSemaphore.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();

            var result = await Task.Run(() => _plc!.ReadBytes(S7.Net.DataType.DataBlock, dbNumber, startByte, count));
            return result;
        }
        catch (Exception ex)
        {
            OnError($"读取字节数组 DB{dbNumber}.DBB{startByte} ({count}字节) 失败: {ex.Message}");
            throw; // 向上传播异常，让调用方正确标记变量为 Error
        }
        finally
        {
            _plcSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<string> ReadStringAsync(int dbNumber, int startByte, int maxLength = 254, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        try
        {
            // S7 String 格式：第1字节=最大长度，第2字节=实际长度，后面是字符数据
            var bytes = await ReadBytesAsync(dbNumber, startByte, maxLength + 2);
            if (bytes.Length < 2) return "";

            int actualLength = bytes[1];
            if (actualLength > maxLength) actualLength = maxLength;
            if (actualLength <= 0) return "";

            return System.Text.Encoding.ASCII.GetString(bytes, 2, actualLength);
        }
        catch (Exception ex)
        {
            OnError($"读取字符串 DB{dbNumber}.DBB{startByte} 失败: {ex.Message}");
            return "";
        }
    }

    /// <inheritdoc/>
    public async Task WriteByteAsync(string address, byte value)
    {
        await WriteAsync(address, value);
    }

    /// <inheritdoc/>
    public async Task WriteBytesAsync(int dbNumber, int startByte, byte[] data, CancellationToken cancellationToken = default)
    {
        await _plcSemaphore.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();

            await Task.Run(() => _plc!.WriteBytes(S7.Net.DataType.DataBlock, dbNumber, startByte, data));
        }
        catch (Exception ex)
        {
            OnError($"写入字节数组 DB{dbNumber}.DBB{startByte} 失败: {ex.Message}");
            throw;
        }
        finally
        {
            _plcSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task WriteStringAsync(int dbNumber, int startByte, string value, int maxLength = 254, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        try
        {
            // S7 String 格式：第1字节=最大长度，第2字节=实际长度，后面是字符数据
            var stringBytes = System.Text.Encoding.ASCII.GetBytes(value);
            int actualLength = Math.Min(stringBytes.Length, maxLength);

            var data = new byte[actualLength + 2];
            data[0] = (byte)maxLength;
            data[1] = (byte)actualLength;
            Array.Copy(stringBytes, 0, data, 2, actualLength);

            await WriteBytesAsync(dbNumber, startByte, data);
        }
        catch (Exception ex)
        {
            OnError($"写入字符串 DB{dbNumber}.DBB{startByte} 失败: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<List<PlcDataItem>> ReadMultipleAsync(List<PlcDataItem> items, CancellationToken cancellationToken = default)
    {
        // ReadAsync 已有 Semaphore 保护，此处不再重复加锁
        // 仅在入口检查连接状态
        if (!IsConnected || _plc == null)
            throw new InvalidOperationException("PLC 未连接");

        foreach (var item in items)
        {
            try
            {
                item.Value = item.DataType switch
                {
                    PlcDataType.Bool => await ReadAsync<bool>(item.Address),
                    PlcDataType.Int => await ReadAsync<short>(item.Address),
                    PlcDataType.DInt => await ReadAsync<int>(item.Address),
                    PlcDataType.Real => await ReadAsync<float>(item.Address),
                    _ => await ReadAsync<object>(item.Address)
                };
                item.LastUpdateTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                OnError($"读取 {item.Name} ({item.Address}) 失败: {ex.Message}");
            }
        }

        return items;
    }

    private void EnsureConnected()
    {
        if (!IsConnected || _plc == null)
        {
            throw new InvalidOperationException("PLC 未连接");
        }
    }

    private void OnError(string message)
    {
        ErrorOccurred?.Invoke(this, message);
    }

    private void StartHeartbeat()
    {
        StopHeartbeat();
        _heartbeatTimer = new System.Timers.Timer(1000); // 1秒心跳
        _heartbeatTimer.Elapsed += async (s, e) => await OnHeartbeatAsync();
        _heartbeatTimer.Start();
    }

    private void StopHeartbeat()
    {
        _heartbeatTimer?.Stop();
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    private async Task OnHeartbeatAsync()
    {
        if (_plc == null) return;

        _heartbeatTimer?.Stop();

        try
        {
            bool isPingSuccess = await CheckHeartbeatPingAsync();
            var plc = _plc;
            if (plc == null) return;

            bool isPlcConnected = plc.IsConnected;
            bool currentlyConnected = isPingSuccess && isPlcConnected;

            // 仅在状态变化时记录日志
            if (_lastHeartbeatConnected != currentlyConnected)
            {
                Log(currentlyConnected
                    ? $"心跳: 连接已恢复 (Ping={PingLatency}ms)"
                    : $"心跳: 连接断开 (Ping={isPingSuccess}, S7={isPlcConnected})");
                _lastHeartbeatConnected = currentlyConnected;
            }

            if (currentlyConnected)
            {
                if (!IsConnected) IsConnected = true;
                _reconnectAttempts = 0;
                await QueryCpuStatusAsync();
            }
            else
            {
                IsConnected = false;
                await TryAutoReconnectAsync();
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            Log($"心跳: 检查异常 - {ex.Message}");
        }
        finally
        {
            if (!_isManuallyDisconnected)
                _heartbeatTimer?.Start();
        }
    }

    /// <summary>
    /// 心跳 Ping 检测：返回是否 Ping 通，并更新延迟数据
    /// </summary>
    private async Task<bool> CheckHeartbeatPingAsync()
    {
        try
        {
            var plc = _plc;
            if (plc == null) return false;

            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = await ping.SendPingAsync(plc.IP, 1000);
            if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
            {
                PingLatency = reply.RoundtripTime;
                LatencyUpdated?.Invoke(this, PingLatency);
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// 查询 PLC CPU Run/Stop 状态（每 N 次心跳执行一次，复用 Sharp7 持久连接）
    /// </summary>
    private async Task QueryCpuStatusAsync()
    {
        _cpuStatusCheckCounter++;
        if (_cpuStatusCheckCounter < CPU_STATUS_CHECK_INTERVAL) return;
        _cpuStatusCheckCounter = 0;

        try
        {
            await Task.Run(() =>
            {
                if (_lastConfig == null) return;

                if (_sharp7Client == null || !_sharp7Client.Connected)
                {
                    _sharp7Client?.Disconnect();
                    _sharp7Client = new Sharp7.S7Client();
                    int connResult = _sharp7Client.ConnectTo(_lastConfig.IpAddress, _lastConfig.Rack, _lastConfig.Slot);
                    if (connResult != 0)
                    {
                        _sharp7Client = null;
                        return;
                    }
                }

                int cpuStatus = 0;
                int statusResult = _sharp7Client.PlcGetStatus(ref cpuStatus);
                if (statusResult == 0)
                {
                    PlcCpuStatus = cpuStatus == 0x08 ? "Run" : (cpuStatus == 0x04 ? "Stop" : "Unknown");
                }
                else
                {
                    PlcCpuStatus = "Unknown";
                    _sharp7Client.Disconnect();
                    _sharp7Client = null;
                }
            });
        }
        catch
        {
            PlcCpuStatus = "Unknown";
            _sharp7Client?.Disconnect();
            _sharp7Client = null;
        }
    }

    /// <summary>
    /// 自动重连逻辑：检查条件后延迟重连，最多重试 MAX_RECONNECT_ATTEMPTS 次
    /// </summary>
    private async Task TryAutoReconnectAsync()
    {
        if (AutoReconnect && !_isManuallyDisconnected && _lastConfig != null && _reconnectAttempts < MAX_RECONNECT_ATTEMPTS)
        {
            _reconnectAttempts++;
            Log($"心跳: 第{_reconnectAttempts}/{MAX_RECONNECT_ATTEMPTS}次自动重连, 等待3秒...");
            await Task.Delay(3000);

            try
            {
                await TryReconnectInternalAsync(_lastConfig);
                if (IsConnected)
                {
                    _reconnectAttempts = 0;
                    Log("心跳: 自动重连成功");
                }
            }
            catch (Exception ex)
            {
                Log($"心跳: 自动重连失败 - {ex.Message}");
            }
        }
        else if (_reconnectAttempts >= MAX_RECONNECT_ATTEMPTS)
        {
            OnError($"已尝试重连{MAX_RECONNECT_ATTEMPTS}次均失败，请检查网络后手动连接");
        }
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            StopHeartbeat();
            _plc?.Close();
            _plc = null;
            _sharp7Client?.Disconnect();
            _sharp7Client = null;
            _plcSemaphore.Dispose();
        }
        _disposed = true;
    }
}
