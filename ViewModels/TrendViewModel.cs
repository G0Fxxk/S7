using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using S7WpfApp.Services;

namespace S7WpfApp.ViewModels;

/// <summary>
/// 单变量趋势窗口 ViewModel — 管理采样数据、统计、采样引擎生命周期
/// View（TrendWindow）仅负责 DrawingVisual 渲染和 UI 交互
/// </summary>
public partial class TrendViewModel : ObservableObject
{
    private readonly IPlcService _plcService;

    // === 变量信息 ===
    public string VariableName { get; }
    public string Address { get; }

    // === 采样数据（环形缓冲区） ===
    public const int MaxPoints = 200;
    public double[] RingBuffer { get; } = new double[MaxPoints];
    public int RingHead { get; private set; }
    public int RingCount { get; private set; }
    public List<(DateTime Time, double Value)> AllSamples { get; } = new();

    // === 统计 ===
    [ObservableProperty] private double _maxValue = double.MinValue;
    [ObservableProperty] private double _minValue = double.MaxValue;
    [ObservableProperty] private double _lastValue;
    [ObservableProperty] private string _avgIntervalText = "--";
    [ObservableProperty] private int _sampleCount;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _intervalMs = 20.0;

    // === S7 地址解析结果 ===
    public int S7DbNumber { get; private set; }
    public int S7StartByte { get; private set; }
    public int S7BitBit { get; private set; }
    public Models.PlcDataType S7DataType { get; private set; } = Models.PlcDataType.Real;

    // === 采样引擎 ===
    private Sharp7SamplingEngine? _samplingEngine;
    private SamplingChannel _samplingChannel = null!;

    /// <summary>
    /// UI 刷新回调（由 View 设置，在 Dispatcher 线程调用 DrawChart）
    /// </summary>
    public Action? OnUiRefreshNeeded { get; set; }

    /// <summary>
    /// 错误回调（由 View 设置，显示错误对话框）
    /// </summary>
    public Action<string>? OnError { get; set; }

    public TrendViewModel(IPlcService plcService, string variableName, string address)
    {
        _plcService = plcService;
        VariableName = variableName;
        Address = address;

        ParseS7Address(address);

        _samplingChannel = new SamplingChannel
        {
            S7DbNumber = S7DbNumber,
            S7StartByte = S7StartByte,
            S7BitBit = S7BitBit,
            DataType = S7DataType,
            OnSample = OnSampleReceived
        };
    }

    /// <summary>
    /// 每次采样回调（后台线程中调用）
    /// </summary>
    private void OnSampleReceived(DateTime sampleTime, double numValue)
    {
        RingBuffer[RingHead] = numValue;
        RingHead = (RingHead + 1) % MaxPoints;
        if (RingCount < MaxPoints) RingCount++;

        AllSamples.Add((sampleTime, numValue));

        if (numValue > MaxValue) MaxValue = numValue;
        if (numValue < MinValue) MinValue = numValue;
    }

    /// <summary>
    /// 开始采样
    /// </summary>
    public void StartSampling()
    {
        _plcService.RequestPauseRefresh();

        _samplingEngine = new Sharp7SamplingEngine(_plcService)
        {
            IntervalMs = IntervalMs,
            OnUiRefreshNeeded = () =>
            {
                // 更新统计数据
                UpdateStats();
                OnUiRefreshNeeded?.Invoke();
            },
            OnError = msg => OnError?.Invoke(msg)
        };

        _samplingEngine.Start(new[] { _samplingChannel });
        IsRunning = true;
    }

    /// <summary>
    /// 停止采样
    /// </summary>
    public void StopSampling()
    {
        _samplingEngine?.Stop();
        IsRunning = false;
        _plcService.RequestResumeRefresh();
    }

    /// <summary>
    /// 清空数据
    /// </summary>
    public void ClearData()
    {
        RingCount = 0;
        RingHead = 0;
        AllSamples.Clear();
        MaxValue = double.MinValue;
        MinValue = double.MaxValue;
        LastValue = 0;
        SampleCount = 0;
        AvgIntervalText = "--";
    }

    /// <summary>
    /// 更新统计文本
    /// </summary>
    public void UpdateStats()
    {
        if (AllSamples.Count >= 2)
        {
            var totalSpan = AllSamples[^1].Time - AllSamples[0].Time;
            double avgMs = totalSpan.TotalMilliseconds / (AllSamples.Count - 1);
            AvgIntervalText = $"{avgMs:F1}ms";
        }

        LastValue = RingCount > 0 ? RingBuffer[(RingHead - 1 + MaxPoints) % MaxPoints] : 0;
        SampleCount = AllSamples.Count;
    }

    /// <summary>
    /// 设置采样间隔
    /// </summary>
    public void ApplyInterval(string text)
    {
        if (int.TryParse(text, out int ms) && ms >= 10)
        {
            IntervalMs = ms;
        }
    }

    /// <summary>
    /// 释放采样引擎资源
    /// </summary>
    public void Dispose()
    {
        _samplingEngine?.Stop();
        _samplingEngine?.Dispose();
        _samplingEngine = null;

        if (IsRunning) _plcService.RequestResumeRefresh();
        IsRunning = false;
    }

    // ═══════════════ 地址解析 ═══════════════

    private void ParseS7Address(string address)
    {
        address = address.ToUpper().Trim();
        if (address.StartsWith("DB"))
        {
            var parts = address.Substring(2).Split('.');
            if (parts.Length >= 2)
            {
                S7DbNumber = int.Parse(parts[0]);
                string offsetPart = parts[1];

                if (offsetPart.StartsWith("DBD"))
                {
                    S7StartByte = int.Parse(offsetPart.Substring(3));
                    S7DataType = Models.PlcDataType.Real;
                }
                else if (offsetPart.StartsWith("DBW"))
                {
                    S7StartByte = int.Parse(offsetPart.Substring(3));
                    S7DataType = Models.PlcDataType.Int;
                }
                else if (offsetPart.StartsWith("DBX"))
                {
                    var bitParts = offsetPart.Substring(3).Split('.');
                    S7StartByte = int.Parse(bitParts[0]);
                    S7BitBit = int.Parse(bitParts[1]);
                    S7DataType = Models.PlcDataType.Bool;
                }
            }
        }
        else if (address.StartsWith("M"))
        {
            S7DbNumber = 0;
            string offsetPart = address.Substring(1);
            if (offsetPart.StartsWith("D"))
            {
                S7StartByte = int.Parse(offsetPart.Substring(1));
                S7DataType = Models.PlcDataType.Real;
            }
            else if (offsetPart.StartsWith("W"))
            {
                S7StartByte = int.Parse(offsetPart.Substring(1));
                S7DataType = Models.PlcDataType.Int;
            }
        }
    }
}
