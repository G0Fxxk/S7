using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using S7WpfApp.Helpers;
using S7WpfApp.ViewModels;

namespace S7WpfApp.Views;

/// <summary>
/// 实时趋势监控窗口 — 使用 DrawingVisual 高性能渲染 + 后台定时器避免 UI 阻塞
/// 业务逻辑委托给 TrendViewModel，此处仅保留渲染和 UI 交互
/// </summary>
public partial class TrendWindow : Window
{
    private readonly TrendViewModel _vm;

    // === 渲染资源（预创建 + Freeze，避免 GC） ===
    private static readonly Pen LinePen;
    private static readonly Pen GridPen;
    private static readonly Brush FillBrush;
    private static readonly Brush BgBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x92, 0xa0));
    private static readonly Brush DotBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xaa));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    static TrendWindow()
    {
        LinePen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xaa)), 2);
        GridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0x40, 0x60, 0x80)), 1);

        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0x40, 0x00, 0xd4, 0xaa), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0x05, 0x00, 0xd4, 0xaa), 1));
        gradient.Freeze();
        FillBrush = gradient;

        LinePen.Freeze();
        GridPen.Freeze();
        BgBrush.Freeze();
        LabelBrush.Freeze();
        DotBrush.Freeze();
    }

    public TrendWindow(TrendViewModel vm)
    {
        _vm = vm;

        InitializeComponent();

        VarNameText.Text = _vm.VariableName;
        VarAddressText.Text = _vm.Address;

        // 设置 ViewModel 的 UI 回调
        _vm.OnUiRefreshNeeded = () =>
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                CurrentValueText.Text = _vm.LastValue.ToString("F2");
                MaxValueText.Text = _vm.MaxValue.ToString("F2");
                MinValueText.Text = _vm.MinValue.ToString("F2");
                AvgIntervalText.Text = _vm.AvgIntervalText;
                SampleCountText.Text = $"采样: {_vm.SampleCount}";
                var memMb = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576.0;
                MemoryText.Text = $"内存: {memMb:F1} MB";
                DrawChart();
            });
        };

        _vm.OnError = msg =>
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(msg, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                _vm.StopSampling();
                StartStopBtn.Content = "▶ 开始";
            });
        };

        Closed += (_, _) => _vm.Dispose();
    }

    // ═══════════════ 高性能图表渲染 ═══════════════

    private void DrawChart()
    {
        double w = ChartBorder.ActualWidth - 4;
        double h = ChartBorder.ActualHeight - 4;
        if (w < 20 || h < 20 || _vm.RingCount < 2) return;

        double margin = 8;
        double chartW = w - margin * 2;
        double chartH = h - margin * 2;

        // 计算 Y 轴范围
        double yMin = double.MaxValue, yMax = double.MinValue;
        for (int i = 0; i < _vm.RingCount; i++)
        {
            int idx = (_vm.RingHead - _vm.RingCount + i + TrendViewModel.MaxPoints) % TrendViewModel.MaxPoints;
            double v = _vm.RingBuffer[idx];
            if (v < yMin) yMin = v;
            if (v > yMax) yMax = v;
        }
        double yRange = yMax - yMin;
        if (yRange < 0.001) yRange = 1;
        yMin -= yRange * 0.1;
        yMax += yRange * 0.1;
        yRange = yMax - yMin;

        var dv = new DrawingVisual();
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));

            // 网格线 + Y 轴标签
            for (int i = 0; i <= 4; i++)
            {
                double y = margin + chartH * i / 4.0;
                dc.DrawLine(GridPen, new Point(margin, y), new Point(w - margin, y));
                double labelVal = yMax - yRange * i / 4.0;
                var ft = new FormattedText(
                    labelVal.ToString("F1"), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, LabelTypeface, 9, LabelBrush, dpi);
                dc.DrawText(ft, new Point(margin + 2, y - 6));
            }

            // 数据点坐标
            double step = chartW / (TrendViewModel.MaxPoints - 1);
            int startIdx = TrendViewModel.MaxPoints - _vm.RingCount;
            var pts = new Point[_vm.RingCount];
            for (int i = 0; i < _vm.RingCount; i++)
            {
                int bufIdx = (_vm.RingHead - _vm.RingCount + i + TrendViewModel.MaxPoints) % TrendViewModel.MaxPoints;
                double x = margin + (startIdx + i) * step;
                double y = margin + chartH * (1 - (_vm.RingBuffer[bufIdx] - yMin) / yRange);
                pts[i] = new Point(x, y);
            }

            // 填充区域
            var fillGeo = new StreamGeometry();
            using (var ctx = fillGeo.Open())
            {
                ctx.BeginFigure(new Point(pts[0].X, margin + chartH), true, true);
                ctx.LineTo(pts[0], false, false);
                for (int i = 1; i < pts.Length; i++)
                    ctx.LineTo(pts[i], false, false);
                ctx.LineTo(new Point(pts[^1].X, margin + chartH), false, false);
            }
            fillGeo.Freeze();
            dc.DrawGeometry(FillBrush, null, fillGeo);

            // 趋势线
            var lineGeo = new StreamGeometry();
            using (var ctx = lineGeo.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                for (int i = 1; i < pts.Length; i++)
                    ctx.LineTo(pts[i], true, false);
            }
            lineGeo.Freeze();
            dc.DrawGeometry(null, LinePen, lineGeo);

            // 最新值标记点
            dc.DrawEllipse(DotBrush, new Pen(Brushes.White, 1), pts[^1], 4, 4);
        }

        int pw = Math.Max(1, (int)(w * dpi));
        int ph = Math.Max(1, (int)(h * dpi));
        var rtb = new RenderTargetBitmap(pw, ph, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        ChartImage.Source = rtb;
    }

    private void OnChartSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_vm.RingCount >= 2) DrawChart();
    }

    // ═══════════════ 控制按钮 ═══════════════

    private void OnStartStopClick(object sender, RoutedEventArgs e)
    {
        if (_vm.IsRunning)
        {
            _vm.StopSampling();
            StartStopBtn.Content = "▶ 开始";
        }
        else
        {
            _vm.ApplyInterval(IntervalBox.Text);
            _vm.StartSampling();
            StartStopBtn.Content = "⏸ 暂停";
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _vm.ClearData();
        CurrentValueText.Text = "--";
        MaxValueText.Text = "--";
        MinValueText.Text = "--";
        SampleCountText.Text = "采样: 0";
        ChartImage.Source = null;
    }

    private void OnIntervalKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) _vm.ApplyInterval(IntervalBox.Text);
    }

    private void OnApplyIntervalClick(object sender, RoutedEventArgs e) => _vm.ApplyInterval(IntervalBox.Text);

    // ═══════════════ PDF/CSV 导出 ═══════════════

    private void OnExportPdfClick(object sender, RoutedEventArgs e)
    {
        if (_vm.AllSamples.Count == 0)
        {
            MessageBox.Show("没有采样数据可导出", "提示", MessageBoxButton.OK);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出趋势报告",
            Filter = "PDF 文件|*.pdf|CSV 数据|*.csv",
            FileName = $"Trend_{_vm.VariableName}_{DateTime.Now:yyyyMMdd_HHmmss}",
            DefaultExt = ".pdf"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            if (dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                ExportCsv(dlg.FileName);
            else
                ExportPdf(dlg.FileName);

            MessageBox.Show($"导出成功！\n{dlg.FileName}\n共 {_vm.AllSamples.Count} 个采样点",
                "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportCsv(string filePath)
    {
        using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
        writer.WriteLine($"# Trend Report: {_vm.VariableName} ({_vm.Address})");
        writer.WriteLine($"# Export Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        writer.WriteLine($"# Samples: {_vm.AllSamples.Count}  Max: {_vm.MaxValue:F2}  Min: {_vm.MinValue:F2}");
        writer.WriteLine("Time,Value");
        foreach (var (time, value) in _vm.AllSamples)
            writer.WriteLine($"{time:yyyy-MM-dd HH:mm:ss.fff},{value:F4}");
    }

    private void ExportPdf(string filePath)
    {
        double avgMs = 0;
        if (_vm.AllSamples.Count >= 2)
        {
            var totalSpan = _vm.AllSamples[^1].Time - _vm.AllSamples[0].Time;
            avgMs = totalSpan.TotalMilliseconds / (_vm.AllSamples.Count - 1);
        }

        using var pdf = new SimplePdfWriter();
        double y = 40;

        pdf.DrawText($"Trend Report: {_vm.VariableName}", 50, y, 18, bold: true, r: 0x00, g: 0x79, b: 0x6B);
        y += 28;

        pdf.DrawText($"Address: {_vm.Address}   Interval: {IntervalBox.Text}ms   Samples: {_vm.AllSamples.Count}", 50, y, 10, r: 0x75, g: 0x75, b: 0x75);
        y += 16;
        pdf.DrawText($"Max: {_vm.MaxValue:F2}   Min: {_vm.MinValue:F2}   Avg Interval: {avgMs:F1}ms   Export: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", 50, y, 10, r: 0x75, g: 0x75, b: 0x75);
        y += 20;

        pdf.DrawLine(50, y, 545);
        y += 12;

        if (ChartImage.Source is RenderTargetBitmap chartBmp)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(chartBmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            var pngBytes = ms.ToArray();

            double imgW = 495;
            double imgH = imgW * chartBmp.PixelHeight / chartBmp.PixelWidth;
            if (imgH > 250) imgH = 250;
            pdf.DrawImage(pngBytes, 50, y, imgW, imgH);
            y += imgH + 15;
        }

        pdf.DrawLine(50, y, 545);
        y += 10;

        pdf.DrawText("No.         Time                    Value", 50, y, 10, bold: true);
        y += 16;
        pdf.DrawLine(50, y, 545);
        y += 6;

        int maxRows = Math.Min(_vm.AllSamples.Count, 50);
        int sampleStep = _vm.AllSamples.Count <= 50 ? 1 : _vm.AllSamples.Count / 50;
        int rowNum = 0;
        for (int i = 0; i < _vm.AllSamples.Count && rowNum < maxRows; i += sampleStep)
        {
            var (time, value) = _vm.AllSamples[i];
            rowNum++;
            pdf.DrawText($"{rowNum,-12}{time:HH:mm:ss.fff}                {value:F4}", 50, y, 9);
            y += 13;
            if (y > 800) break;
        }

        if (_vm.AllSamples.Count > 50)
        {
            y += 6;
            pdf.DrawText($"({_vm.AllSamples.Count} samples total, showing {rowNum} evenly sampled rows. Use CSV for full data.)",
                50, y, 8, r: 0x75, g: 0x75, b: 0x75);
        }

        pdf.Save(filePath);
    }
}
