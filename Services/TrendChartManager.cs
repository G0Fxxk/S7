using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScottPlot;
using ScottPlot.Plottables;
using SkiaSharp;
using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// 多变量趋势图核心管理类，负责 WpfPlot 的渲染、游标系统、缩放交互等逻辑。
/// 剥离自 MultiTrendWindow.xaml.cs，使其瘦身。
/// </summary>
public class TrendChartManager
{
    private readonly StackPanel _plotsPanel;
    private readonly ScrollViewer _plotsScrollViewer;
    private readonly ObservableCollection<TrendChannel> _channels;
    private readonly Action<double> _onViewWindowSecondsChanged;
    private readonly Action<string> _onAvgTimeTextChanged;

    private readonly Dictionary<PlcDataType, ScottPlot.WPF.WpfPlot> _typePlots = new();

    public bool IsAutoScroll { get; set; } = true;
    public double ViewWindowSeconds { get; private set; } = 60.0;
    public bool IsCursorsVisible { get; private set; } = false;

    private bool _isDragging = false;
    private VerticalLine? _draggingLine = null;

    public TrendChartManager(
        StackPanel plotsPanel,
        ScrollViewer plotsScrollViewer,
        ObservableCollection<TrendChannel> channels,
        Action<double> onViewWindowSecondsChanged,
        Action<string> onAvgTimeTextChanged)
    {
        _plotsPanel = plotsPanel;
        _plotsScrollViewer = plotsScrollViewer;
        _channels = channels;
        _onViewWindowSecondsChanged = onViewWindowSecondsChanged;
        _onAvgTimeTextChanged = onAvgTimeTextChanged;

        _plotsScrollViewer.SizeChanged += (s, e) => UpdatePlotHeights();
    }

    /// <summary>
    /// 清理所有图表资源
    /// </summary>
    public void ClearPlots()
    {
        _typePlots.Clear();
        _plotsPanel.Children.Clear();
    }

    /// <summary>
    /// 强制刷新所有图表
    /// </summary>
    public void RefreshAllPlots()
    {
        foreach (var wp in _typePlots.Values)
            wp.Refresh();
    }

    public void SetPlotsInteraction(bool isEnabled)
    {
        foreach (var wp in _typePlots.Values)
            wp.UserInputProcessor.IsEnabled = isEnabled;
    }

    /// <summary>
    /// 自动滚动窗口
    /// </summary>
    public void UpdateSlidingWindow(DateTime samplingStartTime)
    {
        DateTime now = DateTime.Now;
        double elapsed = (now - samplingStartTime).TotalSeconds;

        double xMin, xMax;
        if (elapsed < ViewWindowSeconds)
        {
            xMin = samplingStartTime.ToOADate();
            xMax = samplingStartTime.AddSeconds(ViewWindowSeconds).ToOADate();
        }
        else
        {
            xMax = now.ToOADate();
            xMin = now.AddSeconds(-ViewWindowSeconds).ToOADate();
        }

        foreach (var wp in _typePlots.Values)
        {
            wp.Plot.Axes.SetLimitsX(xMin, xMax);
        }
    }

    /// <summary>
    /// 根据可用空间和图表数量动态设置每个图表的高度
    /// </summary>
    public void UpdatePlotHeights()
    {
        int plotCount = _typePlots.Count;
        if (plotCount == 0) return;

        double availableHeight = _plotsScrollViewer.ActualHeight;
        if (availableHeight <= 0) availableHeight = 400; // fallback

        double plotHeight = Math.Max(200, (availableHeight - plotCount * 4) / plotCount);

        foreach (var wp in _typePlots.Values)
        {
            wp.Height = plotHeight;
        }
    }

    /// <summary>
    /// 增量式按数据类型分组同步图表。
    /// 同一类型（Bool/Int/Real/Byte）的所有选中通道共享一个 WpfPlot。
    /// </summary>
    public void SyncPlots()
    {
        var neededTypes = _channels
            .Where(c => c.IsSelected)
            .Select(c => c.DataType)
            .Distinct()
            .ToHashSet();

        var toRemove = _typePlots.Keys.Where(k => !neededTypes.Contains(k)).ToList();
        foreach (var dt in toRemove)
        {
            _plotsPanel.Children.Remove(_typePlots[dt]);
            _typePlots.Remove(dt);
        }

        foreach (var dt in neededTypes)
        {
            if (!_typePlots.ContainsKey(dt))
            {
                var wp = new ScottPlot.WPF.WpfPlot { Margin = new Thickness(0, 0, 0, 2) };
                wp.Menu?.Clear();
                wp.MouseDown += OnPlotMouseDown;
                wp.MouseMove += OnPlotMouseMove;
                wp.MouseUp += OnPlotMouseUp;
                wp.MouseLeave += OnPlotMouseLeave;
                wp.MouseWheel += OnPlotMouseWheel;

                ApplyIndustrialTheme(wp.Plot);

                if (dt == PlcDataType.Bool)
                {
                    wp.Plot.Axes.SetLimitsY(-0.2, 1.2);
                    wp.Plot.Axes.Left.SetTicks(new double[] { 0, 1 }, new string[] { "0 (False)", "1 (True)" });
                }

                _typePlots[dt] = wp;
                _plotsPanel.Children.Add(wp);
            }
        }

        foreach (var ch in _channels.Where(c => c.IsSelected))
        {
            var wp = _typePlots[ch.DataType];
            ch.PlotControl = wp;

            if (ch.PlotTrace == null)
            {
                ch.PlotTrace = wp.Plot.Add.SignalXY(ch.XData, ch.YData);
                var wpfColor = ((SolidColorBrush)ch.LinePen.Brush).Color;
                ch.PlotTrace.Color = ScottPlot.Color.FromARGB((uint)((wpfColor.A << 24) | (wpfColor.R << 16) | (wpfColor.G << 8) | wpfColor.B));
                ch.PlotTrace.LineWidth = 2;
                ch.PlotTrace.LegendText = ch.Name;
            }
        }

        foreach (var ch in _channels.Where(c => !c.IsSelected))
        {
            if (ch.PlotTrace != null && ch.PlotControl != null)
            {
                ch.PlotControl.Plot.Remove(ch.PlotTrace);
                ch.PlotTrace = null;
            }
            ch.PlotControl = null;
        }

        if (IsCursorsVisible)
            RestoreCursorsOnPlot();

        UpdatePlotHeights();
    }

    public void ZoomTimeWindow(double multiplier, DateTime samplingStartTime)
    {
        ViewWindowSeconds = Math.Clamp(ViewWindowSeconds * multiplier, 1.0, 3600.0);
        _onViewWindowSecondsChanged?.Invoke(ViewWindowSeconds);
        if (IsAutoScroll)
        {
            UpdateSlidingWindow(samplingStartTime);
            RefreshAllPlots();
        }
    }

    public void ResetView()
    {
        IsAutoScroll = true;
        ViewWindowSeconds = 60.0;
        _onViewWindowSecondsChanged?.Invoke(ViewWindowSeconds);

        SetPlotsInteraction(true);

        foreach (var kv in _typePlots)
        {
            if (kv.Key != PlcDataType.Bool)
                kv.Value.Plot.Axes.AutoScaleY();
        }
        RefreshAllPlots();
    }

    public void AutoScaleY()
    {
        foreach (var kv in _typePlots)
        {
            if (kv.Key != PlcDataType.Bool)
                kv.Value.Plot.Axes.AutoScaleY();
            if (!IsAutoScroll)
                kv.Value.Plot.Axes.AutoScaleX();
        }
        RefreshAllPlots();
    }

    // ═══════════════ ScottPlot 工业主题 ═══════════════

    private void ApplyIndustrialTheme(ScottPlot.Plot plot)
    {
        var darkBg = ScottPlot.Color.FromHex("#1e1e1e");
        var dataBg = ScottPlot.Color.FromHex("#2d2d2d");

        plot.FigureBackground.Color = darkBg;
        plot.DataBackground.Color = dataBg;

        ScottPlot.Color foreColor = ScottPlot.Color.FromHex("#d7d7d7");

        plot.Axes.Bottom.Label.ForeColor = foreColor;
        plot.Axes.Left.Label.ForeColor = foreColor;
        plot.Axes.Title.Label.ForeColor = foreColor;
        plot.Axes.Bottom.TickLabelStyle.ForeColor = foreColor;
        plot.Axes.Left.TickLabelStyle.ForeColor = foreColor;

        plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#404040");
        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.DateTimeAutomatic();

        plot.Legend.IsVisible = true;
        plot.Legend.FontSize = 12;
        plot.Legend.BackgroundColor = dataBg;
        plot.Legend.OutlineColor = foreColor;
        plot.Legend.FontColor = foreColor;
        plot.Legend.ShadowColor = ScottPlot.Colors.Transparent;

        try
        {
            string fontName = "Microsoft YaHei";
            var tf = SKFontManager.Default.MatchCharacter('中');
            if (tf != null) fontName = tf.FamilyName;

            plot.Axes.Title.Label.FontName = fontName;
            plot.Axes.Left.Label.FontName = fontName;
            plot.Axes.Bottom.Label.FontName = fontName;
            plot.Axes.Left.TickLabelStyle.FontName = fontName;
            plot.Axes.Bottom.TickLabelStyle.FontName = fontName;
            plot.Legend.FontName = fontName;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"图表字体设置异常: {ex.Message}"); }
    }

    // ══════════════?游标系统 & 鼠标事件 ══════════════?

    public void ToggleCursors()
    {
        IsCursorsVisible = !IsCursorsVisible;

        if (IsCursorsVisible)
        {
            var firstPlot = _channels.FirstOrDefault(c => c.IsSelected && c.PlotControl != null)?.PlotControl;
            if (firstPlot == null)
            {
                IsCursorsVisible = false;
                return;
            }

            var limits = firstPlot.Plot.Axes.GetLimits();
            double range = limits.Right - limits.Left;
            if (range == 0) range = 1.0;

            double x1 = limits.Left + range * 0.33;
            double x2 = limits.Left + range * 0.66;

            foreach (var ch in _channels.Where(c => c.IsSelected && c.PlotControl != null))
            {
                if (ch.CursorA != null) ch.PlotControl!.Plot.Remove(ch.CursorA);
                if (ch.CursorB != null) ch.PlotControl!.Plot.Remove(ch.CursorB);

                ch.CursorA = ch.PlotControl!.Plot.Add.VerticalLine(x1);
                ch.CursorA.Color = ScottPlot.Color.FromHex("#00FFFF");
                ch.CursorA.LineWidth = 2;
                ch.CursorA.LinePattern = LinePattern.Dashed;
                ch.CursorA.IsDraggable = false;

                ch.CursorB = ch.PlotControl.Plot.Add.VerticalLine(x2);
                ch.CursorB.Color = ScottPlot.Color.FromHex("#FF00FF");
                ch.CursorB.LineWidth = 2;
                ch.CursorB.LinePattern = LinePattern.Dashed;
                ch.CursorB.IsDraggable = false;
            }

            IsAutoScroll = false;
            SetPlotsInteraction(false);
        }
        else
        {
            foreach (var ch in _channels.Where(c => c.IsSelected && c.PlotControl != null))
            {
                if (ch.CursorA != null) { ch.PlotControl!.Plot.Remove(ch.CursorA); ch.CursorA = null; }
                if (ch.CursorB != null) { ch.PlotControl!.Plot.Remove(ch.CursorB); ch.CursorB = null; }
                ch.PlotControl!.Cursor = Cursors.Arrow;
            }
            IsAutoScroll = true;
            SetPlotsInteraction(true);
        }
        RefreshAllPlots();
    }

    private void RestoreCursorsOnPlot()
    {
        if (!IsCursorsVisible) return;
        var firstPlot = _channels.FirstOrDefault(c => c.IsSelected && c.PlotControl != null && c.CursorA != null)?.PlotControl;
        double x1 = 0, x2 = 1;

        if (firstPlot != null)
        {
            var chFirst = _channels.First(c => c.PlotControl == firstPlot);
            x1 = chFirst.CursorA!.X;
            x2 = chFirst.CursorB!.X;
        }

        foreach (var ch in _channels.Where(c => c.IsSelected && c.PlotControl != null))
        {
            if (ch.CursorA == null)
            {
                ch.CursorA = ch.PlotControl!.Plot.Add.VerticalLine(x1);
                ch.CursorA.Color = ScottPlot.Color.FromHex("#00FFFF");
                ch.CursorA.LineWidth = 2;
                ch.CursorA.LinePattern = LinePattern.Dashed;
                ch.CursorA.IsDraggable = false;
            }
            if (ch.CursorB == null)
            {
                ch.CursorB = ch.PlotControl!.Plot.Add.VerticalLine(x2);
                ch.CursorB.Color = ScottPlot.Color.FromHex("#FF00FF");
                ch.CursorB.LineWidth = 2;
                ch.CursorB.LinePattern = LinePattern.Dashed;
                ch.CursorB.IsDraggable = false;
            }
        }
    }

    private void UpdateCursorLabels()
    {
        if (!IsCursorsVisible) return;
        var firstCh = _channels.FirstOrDefault(c => c.IsSelected && c.CursorA != null);
        if (firstCh == null || firstCh.CursorA == null || firstCh.CursorB == null) return;

        double xa = firstCh.CursorA.X;
        double xb = firstCh.CursorB.X;

        foreach (var ch in _channels)
        {
            if (ch.XData.Count == 0) continue;

            int idxA = ch.XData.BinarySearch(xa);
            if (idxA < 0) idxA = ~idxA;
            if (idxA >= ch.XData.Count) idxA = ch.XData.Count - 1;

            int idxB = ch.XData.BinarySearch(xb);
            if (idxB < 0) idxB = ~idxB;
            if (idxB >= ch.XData.Count) idxB = ch.XData.Count - 1;

            double ya = ch.YData[idxA];
            double yb = ch.YData[idxB];

            ch.CurrentValueText = $"A:{ya:F1} B:{yb:F1}";
        }

        double deltaT = Math.Abs(DateTime.FromOADate(xb).Subtract(DateTime.FromOADate(xa)).TotalMilliseconds);
        _onAvgTimeTextChanged?.Invoke($"ΔT: {deltaT:F0} ms");
    }

    private bool IsHit(ScottPlot.WPF.WpfPlot plotControl, double lineX, ScottPlot.Pixel mousePixel, out double mouseDataX)
    {
        var mouseCoords = plotControl.Plot.GetCoordinates(mousePixel);
        mouseDataX = mouseCoords.X;

        var pixelPlus10 = new ScottPlot.Pixel(mousePixel.X + 10, mousePixel.Y);
        var coordsPlus10 = plotControl.Plot.GetCoordinates(pixelPlus10);

        double thresholdTime = Math.Abs(coordsPlus10.X - mouseCoords.X);
        return Math.Abs(lineX - mouseDataX) <= thresholdTime;
    }

    private void StartDrag()
    {
        _isDragging = true;
        foreach (var c in _channels.Where(x => x.PlotControl != null))
            c.PlotControl!.Cursor = Cursors.SizeWE;
    }

    private void OnPlotMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsCursorsVisible || e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not ScottPlot.WPF.WpfPlot wp) return;

        var ch = _channels.FirstOrDefault(c => c.PlotControl == wp);
        if (ch?.CursorA == null || ch?.CursorB == null) return;

        var pos = e.GetPosition(wp);
        ScottPlot.Pixel mousePixel = new ScottPlot.Pixel((float)pos.X, (float)pos.Y);

        bool hitA = IsHit(wp, ch.CursorA.X, mousePixel, out _);
        bool hitB = IsHit(wp, ch.CursorB.X, mousePixel, out _);

        if (hitA)
        {
            _draggingLine = ch.CursorA;
            StartDrag();
        }
        else if (hitB)
        {
            _draggingLine = ch.CursorB;
            StartDrag();
        }
    }

    private void OnPlotMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsCursorsVisible) return;
        if (sender is not ScottPlot.WPF.WpfPlot wp) return;

        var chSource = _channels.FirstOrDefault(c => c.PlotControl == wp);
        if (chSource == null) return;

        var pos = e.GetPosition(wp);
        ScottPlot.Pixel mousePixel = new ScottPlot.Pixel((float)pos.X, (float)pos.Y);

        if (_isDragging && _draggingLine != null)
        {
            var mouseLoc = wp.Plot.GetCoordinates(mousePixel);
            double targetX = mouseLoc.X;

            var fc = _channels.FirstOrDefault(c => c.XData.Count > 0);
            if (fc != null)
            {
                int idx = fc.XData.BinarySearch(targetX);
                if (idx < 0) idx = ~idx;
                if (idx >= fc.XData.Count) idx = fc.XData.Count - 1;

                if (idx > 0 && Math.Abs(fc.XData[idx - 1] - targetX) < Math.Abs(fc.XData[idx] - targetX))
                    idx--;
                if (idx >= 0) targetX = fc.XData[idx];
            }

            bool isDraggingA = _channels.Any(c => c.CursorA == _draggingLine);

            foreach (var c in _channels.Where(x => x.IsSelected))
            {
                if (isDraggingA && c.CursorA != null) c.CursorA.X = targetX;
                else if (!isDraggingA && c.CursorB != null) c.CursorB.X = targetX;
            }

            UpdateCursorLabels();
            RefreshAllPlots();
            return;
        }

        if (chSource.CursorA == null || chSource.CursorB == null) return;

        bool hitA = IsHit(wp, chSource.CursorA.X, mousePixel, out _);
        bool hitB = IsHit(wp, chSource.CursorB.X, mousePixel, out _);

        wp.Cursor = (hitA || hitB) ? Cursors.SizeWE : Cursors.Arrow;
    }

    private void OnPlotMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            _draggingLine = null;
            foreach (var c in _channels.Where(x => x.PlotControl != null))
                c.PlotControl!.Cursor = Cursors.Arrow;
            UpdateCursorLabels();
        }
    }

    private void OnPlotMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            _draggingLine = null;
            foreach (var c in _channels.Where(x => x.PlotControl != null))
                c.PlotControl!.Cursor = Cursors.Arrow;
            UpdateCursorLabels();
        }
    }

    private void OnPlotMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsAutoScroll)
        {
            double multiplier = e.Delta > 0 ? 0.8 : 1.25;
            // Since we don't naturally have samplingStartTime here without passing it inside TrendChartManager differently,
            // We can just rely on DateTime.Now backwards, or let caller handle the wheel via an event if necessary.
            // For now, let's keep a public property or call a generic action for zooming if we strictly need samplingStartTime.
            // A simpler fix is to disable zooming while auto scrolling. Wait, MultiTrendWindow allowed ZoomTimeWindow in wheel:
            // "e.Delta > 0 向上滚代表放大"
            // To properly resolve samplingStartTime, we can store it in TrendChartManager once started.
            // However, this logic will be invoked but lacks samplingStartTime at this precise moment.
            // Assuming I will just update the view window size, and the main sampling loop will call UpdateSlidingWindow shortly.
            ViewWindowSeconds = Math.Clamp(ViewWindowSeconds * multiplier, 1.0, 3600.0);
            _onViewWindowSecondsChanged?.Invoke(ViewWindowSeconds);
            RefreshAllPlots();

            e.Handled = true;
        }
    }
}
