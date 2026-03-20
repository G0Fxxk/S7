using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;
using ScottPlot.Plottables;
using ScottPlot.WPF;

namespace S7WpfApp.Models;

/// <summary>
/// 多变量趋势采样通道模型
/// </summary>
public partial class TrendChannel : ObservableObject
{
    private const int RingSize = 200;

    /// <summary>
    /// 可配置的最大数据点数（超出时裁剪前 10%）
    /// </summary>
    public static int MaxDataPoints { get; set; } = 50000;

    /// <summary>
    /// 变量名（中文名或符号）
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// PLC 绝对地址 (如 DB100.DBD0, M10.0)
    /// </summary>
    [ObservableProperty]
    private string _address = string.Empty;

    /// <summary>
    /// 数据类型
    /// </summary>
    [ObservableProperty]
    private PlcDataType _dataType = PlcDataType.Real;

    /// <summary>
    /// 分配的主题颜色画笔 (DrawingVisual 渲染用)
    /// </summary>
    [ObservableProperty]
    private Pen _linePen;

    // --- 高性能环形缓冲，无需触发绑定，只供后端渲染引擎极速读取 ---
    public double[] RingBuffer { get; } = new double[RingSize];
    public int RingHead { get; set; } = 0;
    public int RingCount { get; set; } = 0;
    public double MaxValue { get; set; } = double.MinValue;
    public double MinValue { get; set; } = double.MaxValue;

    // 独立的历史采样集合（可选功能，用于导出）
    public List<(DateTime Time, double Value)> AllSamples { get; } = new();

    // --- ScottPlot 专用数据列表 ---
    public List<double> XData { get; } = new(50000);
    public List<double> YData { get; } = new(50000);
    public SignalXY? PlotTrace { get; set; }

    // Sharp7 解析后缓存，避免每次读取重新切割字符串 (消除 GC)
    public int S7DbNumber { get; set; }
    public int S7StartByte { get; set; }
    public int S7BitBit { get; set; }

    // --- 界面显示所需属性 ---

    /// <summary>
    /// 是否选中（开启独立图表并采样）
    /// </summary>
    [ObservableProperty]
    private bool _isSelected = true;

    // 节流：仅在值变化时才触发 PropertyChanged
    private string _currentValueText = "--";
    public string CurrentValueText
    {
        get => _currentValueText;
        set
        {
            if (!string.Equals(_currentValueText, value, StringComparison.Ordinal))
            {
                _currentValueText = value;
                OnPropertyChanged();
            }
        }
    }

    // --- WPF 控件绑定（代码后置管理） ---
    public WpfPlot? PlotControl { get; set; }
    public VerticalLine? CursorA { get; set; }
    public VerticalLine? CursorB { get; set; }

    // 节流：仅在值变化时才触发 PropertyChanged
    private string _minMaxText = "-- / --";
    public string MinMaxText
    {
        get => _minMaxText;
        set
        {
            if (!string.Equals(_minMaxText, value, StringComparison.Ordinal))
            {
                _minMaxText = value;
                OnPropertyChanged();
            }
        }
    }

    public TrendChannel(string name, string address, Color curveColor)
    {
        Name = name;
        Address = address;
        LinePen = new Pen(new SolidColorBrush(curveColor), 1.5);
        LinePen.Freeze(); // 冻结以提升渲染性能
    }

    /// <summary>
    /// 解析地址以供 Sharp7 极速使用
    /// </summary>
    public bool TryParseAddress()
    {
        try
        {
            string addr = Address.ToUpper().Trim();
            if (addr.StartsWith("DB"))
            {
                var parts = addr.Substring(2).Split('.');
                if (parts.Length >= 2)
                {
                    S7DbNumber = int.Parse(parts[0]);
                    string offsetPart = parts[1];

                    if (offsetPart.StartsWith("DBD"))
                    {
                        S7StartByte = int.Parse(offsetPart.Substring(3));
                        DataType = PlcDataType.Real;
                    }
                    else if (offsetPart.StartsWith("DBW"))
                    {
                        S7StartByte = int.Parse(offsetPart.Substring(3));
                        DataType = PlcDataType.Int;
                    }
                    else if (offsetPart.StartsWith("DBX"))
                    {
                        var bitParts = offsetPart.Substring(3).Split('.');
                        S7StartByte = int.Parse(bitParts[0]);
                        S7BitBit = int.Parse(bitParts[1]);
                        DataType = PlcDataType.Bool;
                    }
                    else if (offsetPart.StartsWith("DBB"))
                    {
                        S7StartByte = int.Parse(offsetPart.Substring(3));
                        DataType = PlcDataType.Byte;
                    }
                    return true;
                }
            }
            else if (addr.StartsWith("M"))
            {
                S7DbNumber = 0;
                string offsetPart = addr.Substring(1);

                if (offsetPart.StartsWith("D"))
                {
                    S7StartByte = int.Parse(offsetPart.Substring(1));
                    DataType = PlcDataType.Real;
                }
                else if (offsetPart.StartsWith("W"))
                {
                    S7StartByte = int.Parse(offsetPart.Substring(1));
                    DataType = PlcDataType.Int;
                }
                else if (offsetPart.Contains("."))
                {
                    var bitParts = offsetPart.Replace("X", "").Split('.');
                    S7StartByte = int.Parse(bitParts[0]);
                    S7BitBit = int.Parse(bitParts[1]);
                    DataType = PlcDataType.Bool;
                }
                else if (offsetPart.StartsWith("B"))
                {
                    S7StartByte = int.Parse(offsetPart.Substring(1));
                    DataType = PlcDataType.Byte;
                }
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public void AddSample(DateTime time, double value)
    {
        RingBuffer[RingHead] = value;
        RingHead = (RingHead + 1) % RingSize;
        if (RingCount < RingSize) RingCount++;

        AllSamples.Add((time, value));

        // ScottPlot 数据同步写入
        XData.Add(time.ToOADate());
        YData.Add(value);

        if (value > MaxValue) MaxValue = value;
        if (value < MinValue) MinValue = value;

        // 超出可配置上限时裁剪前 10%
        if (XData.Count > MaxDataPoints)
        {
            int trimCount = MaxDataPoints / 10;
            XData.RemoveRange(0, trimCount);
            YData.RemoveRange(0, trimCount);
            AllSamples.RemoveRange(0, trimCount);
        }
    }

    public void ClearData()
    {
        RingCount = 0;
        RingHead = 0;
        AllSamples.Clear();
        XData.Clear();
        YData.Clear();
        MaxValue = double.MinValue;
        MinValue = double.MaxValue;
        CurrentValueText = "--";
        MinMaxText = "-- / --";
    }
}
