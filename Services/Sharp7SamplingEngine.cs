using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// 采样通道定义 — 描述单个变量在 Sharp7 中的读取地址
/// </summary>
public class SamplingChannel
{
    public int S7DbNumber { get; set; }
    public int S7StartByte { get; set; }
    public int S7BitBit { get; set; }
    public PlcDataType DataType { get; set; } = PlcDataType.Real;

    /// <summary>每次采样后对数值的回调</summary>
    public Action<DateTime, double>? OnSample { get; set; }

    /// <summary>根据数据类型计算需要读取的字节数</summary>
    public int BytesToRead => DataType switch
    {
        PlcDataType.Real => 4,
        PlcDataType.Int => 2,
        PlcDataType.DInt => 4,
        _ => 1
    };
}

/// <summary>
/// Sharp7 高速采样引擎 — 统一的后台采样核心，
/// 从 TrendWindow 和 MultiTrendWindow 的重复实现中提取
/// </summary>
public class Sharp7SamplingEngine : IDisposable
{
    private readonly IPlcService _plcService;
    private Sharp7.S7Client? _client;
    private CancellationTokenSource? _cts;
    private Task? _task;

    /// <summary>是否正在运行</summary>
    public bool IsRunning { get; private set; }

    /// <summary>采样间隔（毫秒）</summary>
    public double IntervalMs { get; set; } = 20.0;

    /// <summary>UI 降频刷新间隔（毫秒）</summary>
    public int UiRefreshIntervalMs { get; set; } = 50;

    /// <summary>每次 UI 降频刷新时回调（在后台线程调用，调用方自行 Dispatcher）</summary>
    public Action? OnUiRefreshNeeded { get; set; }

    /// <summary>启动失败或运行时错误回调</summary>
    public Action<string>? OnError { get; set; }

    public Sharp7SamplingEngine(IPlcService plcService)
    {
        _plcService = plcService;
    }

    /// <summary>
    /// 启动采样引擎
    /// </summary>
    /// <param name="channels">采样通道列表</param>
    public bool Start(IReadOnlyList<SamplingChannel> channels)
    {
        if (IsRunning || channels.Count == 0) return false;

        var config = _plcService.GetCurrentConfig();
        if (config == null)
        {
            OnError?.Invoke("未连接 PLC，无法启动高速采样。");
            return false;
        }

        _cts = new CancellationTokenSource();
        IsRunning = true;

        if (channels.Count == 1)
        {
            // 单通道模式 — 使用 DBRead/MBRead（轻量）
            _task = Task.Run(() => RunSingleChannel(channels[0], config, _cts.Token), _cts.Token);
        }
        else
        {
            // 多通道模式 — 使用 ReadMultiVars + GCHandle Pinned（零分配）
            _task = Task.Run(() => RunMultiChannel(channels, config, _cts.Token), _cts.Token);
        }
        return true;
    }

    /// <summary>
    /// 停止采样引擎并断开 Sharp7 连接
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _client?.Disconnect();
        _client = null;
        IsRunning = false;
    }

    // ═══════════════ 单通道采样 ═══════════════

    private void RunSingleChannel(SamplingChannel ch, PlcConnectionConfig config, CancellationToken token)
    {
        _client = new Sharp7.S7Client();
        int connResult = _client.ConnectTo(config.IpAddress, config.Rack, config.Slot);
        if (connResult != 0)
        {
            OnError?.Invoke($"Sharp7 高速通道连接失败: {_client.ErrorText(connResult)}");
            IsRunning = false;
            return;
        }

        byte[] buffer = new byte[8]; // 复用缓冲区，大于最大基础类型
        var sw = Stopwatch.StartNew();
        double nextTime = IntervalMs;
        int lastUiTick = Environment.TickCount;

        while (!token.IsCancellationRequested)
        {
            // SpinWait 精准等待
            while (sw.Elapsed.TotalMilliseconds < nextTime)
            {
                Thread.SpinWait(10);
                if (token.IsCancellationRequested) return;
            }
            nextTime += IntervalMs;
            var sampleTime = DateTime.Now;

            // Sharp7 零分配读取
            int readResult;
            if (ch.S7DbNumber > 0)
                readResult = _client.DBRead(ch.S7DbNumber, ch.S7StartByte, ch.BytesToRead, buffer);
            else
                readResult = _client.MBRead(ch.S7StartByte, ch.BytesToRead, buffer);

            if (readResult == 0)
            {
                double value = ParseValue(buffer, ch);
                ch.OnSample?.Invoke(sampleTime, value);

                // 降频 UI 刷新
                if (Environment.TickCount - lastUiTick >= UiRefreshIntervalMs)
                {
                    lastUiTick = Environment.TickCount;
                    OnUiRefreshNeeded?.Invoke();
                }
            }
        }
    }

    // ═══════════════ 多通道采样 ═══════════════

    private void RunMultiChannel(IReadOnlyList<SamplingChannel> channels, PlcConnectionConfig config, CancellationToken token)
    {
        _client = new Sharp7.S7Client();
        int connResult = _client.ConnectTo(config.IpAddress, config.Rack, config.Slot);
        if (connResult != 0)
        {
            OnError?.Invoke($"Sharp7 高速通道连接失败: {_client.ErrorText(connResult)}");
            IsRunning = false;
            return;
        }

        int n = channels.Count;
        var s7Items = new Sharp7.S7Client.S7DataItem[n];
        var handles = new GCHandle[n];
        var buffers = new byte[n][];

        try
        {
            // 预分配 Pinned 缓冲区
            for (int i = 0; i < n; i++)
            {
                var ch = channels[i];
                buffers[i] = new byte[ch.BytesToRead];
                handles[i] = GCHandle.Alloc(buffers[i], GCHandleType.Pinned);

                s7Items[i] = new Sharp7.S7Client.S7DataItem
                {
                    Amount = ch.BytesToRead,
                    WordLen = (int)Sharp7.S7WordLength.Byte,
                    pData = handles[i].AddrOfPinnedObject(),
                    Start = (ch.S7StartByte * 8) + ch.S7BitBit
                };

                if (ch.S7DbNumber > 0)
                {
                    s7Items[i].Area = (int)Sharp7.S7Area.DB;
                    s7Items[i].DBNumber = ch.S7DbNumber;
                }
                else
                {
                    s7Items[i].Area = (int)Sharp7.S7Area.MK;
                    s7Items[i].DBNumber = 0;
                }
            }

            var sw = Stopwatch.StartNew();
            double nextTime = IntervalMs;
            int lastUiTick = Environment.TickCount;

            while (!token.IsCancellationRequested)
            {
                // 混合等待：> 2ms 用 Sleep(1)，否则 SpinOnce
                var spinWait = new SpinWait();
                while (sw.Elapsed.TotalMilliseconds < nextTime)
                {
                    double remain = nextTime - sw.Elapsed.TotalMilliseconds;
                    if (remain > 2.0)
                        Thread.Sleep(1);
                    else
                        spinWait.SpinOnce();
                    if (token.IsCancellationRequested) return;
                }

                // 防止积压
                if (sw.Elapsed.TotalMilliseconds - nextTime > IntervalMs * 5)
                    nextTime = sw.Elapsed.TotalMilliseconds;

                nextTime += IntervalMs;
                var sampleTime = DateTime.Now;

                // ReadMultiVars 一次读取所有通道
                int readResult = _client.ReadMultiVars(s7Items, n);
                if (readResult == 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (s7Items[i].Result == 0)
                        {
                            double value = ParseValue(buffers[i], channels[i]);
                            channels[i].OnSample?.Invoke(sampleTime, value);
                        }
                    }
                }

                // 降频 UI 刷新
                if (Environment.TickCount - lastUiTick >= UiRefreshIntervalMs)
                {
                    lastUiTick = Environment.TickCount;
                    OnUiRefreshNeeded?.Invoke();
                }
            }
        }
        finally
        {
            for (int i = 0; i < n; i++)
            {
                if (handles[i].IsAllocated)
                    handles[i].Free();
            }
        }
    }

    // ═══════════════ 值解析 ═══════════════

    private static double ParseValue(byte[] buffer, SamplingChannel ch)
    {
        return ch.DataType switch
        {
            PlcDataType.Real => Sharp7.S7.GetRealAt(buffer, 0),
            PlcDataType.Int => Sharp7.S7.GetIntAt(buffer, 0),
            PlcDataType.DInt => Sharp7.S7.GetDIntAt(buffer, 0),
            PlcDataType.Bool => Sharp7.S7.GetBitAt(buffer, 0, ch.S7BitBit) ? 1.0 : 0.0,
            PlcDataType.Byte => buffer[0],
            _ => 0
        };
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
