using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using S7WpfApp.Models;
using S7WpfApp.Services;

namespace S7WpfApp.ViewModels;

/// <summary>
/// 多通道趋势窗口 ViewModel — 管理通道集合、采样配置和运行状态
/// ScottPlot 渲染逻辑保留在 View 层
/// </summary>
public partial class MultiTrendViewModel : ObservableObject
{
    private readonly IPlcService _plcService;
    private readonly ISymbolService _symbolService;

    public ObservableCollection<TrendChannel> Channels { get; } = new();

    // 调色板
    private static readonly System.Drawing.Color[] Palette =
    {
        System.Drawing.Color.FromArgb(0x00, 0xFF, 0xFF), // Cyan
        System.Drawing.Color.FromArgb(0xFF, 0x52, 0x52), // Bright Red
        System.Drawing.Color.FromArgb(0x69, 0xF0, 0xAE), // Bright Green
        System.Drawing.Color.FromArgb(0xFF, 0xD7, 0x40), // Amber
        System.Drawing.Color.FromArgb(0x44, 0x8A, 0xFF), // Bright Blue
        System.Drawing.Color.FromArgb(0xE0, 0x40, 0xFB), // Purple
        System.Drawing.Color.FromArgb(0xFF, 0xA7, 0x26)  // Orange
    };
    private int _colorIndex;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private double _intervalMs = 20.0;

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private string _sampleCountText = "采样: 0";

    [ObservableProperty]
    private string _memoryText = "内存: 0 MB";

    [ObservableProperty]
    private string _avgTimeText = "Avg: 0 ms";

    [ObservableProperty]
    private DateTime _samplingStartTime;

    public IPlcService PlcService => _plcService;
    public ISymbolService SymbolService => _symbolService;

    public MultiTrendViewModel(IPlcService plcService, ISymbolService symbolService)
    {
        _plcService = plcService;
        _symbolService = symbolService;
    }

    /// <summary>
    /// 添加监控通道（返回错误信息，null 表示成功）
    /// </summary>
    public string? AddChannel(string name, string address)
    {
        if (IsRunning)
            return "请先暂停采样后再添加新通道";

        if (string.IsNullOrWhiteSpace(address))
            return "未输入有效的 PLC 地址";

        if (string.IsNullOrWhiteSpace(name))
            name = address;

        var drawColor = Palette[_colorIndex % Palette.Length];
        _colorIndex++;

        var wpfColor = System.Windows.Media.Color.FromRgb(drawColor.R, drawColor.G, drawColor.B);
        var channel = new TrendChannel(name, address, wpfColor);
        if (!channel.TryParseAddress())
            return $"无法识别地址格式: {address}。支持如 DB10.DBD0, M10.2等。";

        if (Channels.Any(c => c.Address.Equals(address, StringComparison.OrdinalIgnoreCase)))
            return $"地址 {address} 已经在监控列表中。";

        Channels.Add(channel);
        return null;
    }

    /// <summary>
    /// 删除通道
    /// </summary>
    public string? RemoveChannel(TrendChannel channel)
    {
        if (IsRunning)
            return "请先暂停采样后再修改通道";

        Channels.Remove(channel);
        return null;
    }

    /// <summary>
    /// 清空所有通道
    /// </summary>
    public bool ClearChannels()
    {
        if (IsRunning) return false;
        Channels.Clear();
        _colorIndex = 0;
        return true;
    }

    /// <summary>
    /// 清除所有通道的数据（不删除通道）
    /// </summary>
    public void ClearData()
    {
        foreach (var ch in Channels)
            ch.ClearData();
    }

    /// <summary>
    /// 应用采样间隔
    /// </summary>
    public bool ApplyInterval(string intervalText)
    {
        if (int.TryParse(intervalText, out int ms) && ms >= 10)
        {
            IntervalMs = ms;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 开始采样，返回采样通道列表供 View 启动引擎
    /// </summary>
    public List<SamplingChannel>? StartSampling()
    {
        if (Channels.Count == 0) return null;

        SamplingStartTime = DateTime.Now;
        IsRunning = true;
        _plcService.RequestPauseRefresh();

        return Channels.Select(ch => new SamplingChannel
        {
            S7DbNumber = ch.S7DbNumber,
            S7StartByte = ch.S7StartByte,
            S7BitBit = ch.S7BitBit,
            DataType = ch.DataType,
            OnSample = (time, value) => ch.AddSample(time, value)
        }).ToList();
    }

    /// <summary>
    /// 停止采样
    /// </summary>
    public void StopSampling()
    {
        IsRunning = false;
        _plcService.RequestResumeRefresh();
    }

    /// <summary>
    /// 更新运行时统计信息
    /// </summary>
    public void UpdateStats()
    {
        var activeChannels = Channels.ToList();
        double avgMs = 0;
        var fc = activeChannels.FirstOrDefault();
        if (fc != null && fc.AllSamples.Count >= 2)
        {
            var totalSpan = fc.AllSamples[^1].Time - fc.AllSamples[0].Time;
            avgMs = totalSpan.TotalMilliseconds / (fc.AllSamples.Count - 1);
        }
        AvgTimeText = $"Avg: {avgMs:F1} ms";

        long totalSamples = activeChannels.Sum(c => (long)c.AllSamples.Count);
        SampleCountText = $"采样: {totalSamples:N0}";
        var memMb = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576.0;
        MemoryText = $"内存: {memMb:F1} MB";

        foreach (var ch in activeChannels)
        {
            if (ch.RingCount > 0)
            {
                int idx = (ch.RingHead - 1 + 200) % 200;
                ch.CurrentValueText = ch.RingBuffer[idx].ToString(ch.DataType == PlcDataType.Real ? "F2" : "F0");
                ch.MinMaxText = $"{ch.MinValue:F1} / {ch.MaxValue:F1}";
            }
        }
    }

    /// <summary>
    /// 获取调色板颜色
    /// </summary>
    public System.Drawing.Color GetPaletteColor(int index) => Palette[index % Palette.Length];
    public System.Drawing.Color[] GetPalette() => Palette;
}
