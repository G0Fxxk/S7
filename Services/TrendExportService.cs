using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using S7WpfApp.Helpers;
using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// 趋势数据导出服务 — 负责 PDF/CSV 导出、数据过滤、表格渲染
/// 从 MultiTrendWindow 中拆分，使导出逻辑可独立测试
/// </summary>
public class TrendExportService
{
    // ═══════════════ 数据过滤 ═══════════════

    /// <summary>
    /// 根据时间范围过滤采样数据（利用 AllSamples 时间有序性使用二分查找）
    /// </summary>
    public List<(DateTime Time, double Value)> FilterSamples(TrendChannel ch, bool useTimeRange, DateTime? start, DateTime? end)
    {
        if (!useTimeRange || start == null || end == null)
            return ch.AllSamples;

        var samples = ch.AllSamples;
        if (samples.Count == 0) return new List<(DateTime, double)>();

        int startIdx = BinarySearchTime(samples, start.Value, searchLower: true);
        int endIdx = BinarySearchTime(samples, end.Value, searchLower: false);

        if (startIdx > endIdx || startIdx >= samples.Count)
            return new List<(DateTime, double)>();

        return samples.GetRange(startIdx, endIdx - startIdx + 1);
    }

    /// <summary>
    /// 在时间有序的采样列表中二分查找
    /// </summary>
    private static int BinarySearchTime(List<(DateTime Time, double Value)> samples, DateTime target, bool searchLower)
    {
        int lo = 0, hi = samples.Count - 1, result = searchLower ? samples.Count : -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (searchLower)
            {
                if (samples[mid].Time >= target) { result = mid; hi = mid - 1; }
                else lo = mid + 1;
            }
            else
            {
                if (samples[mid].Time <= target) { result = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
        }
        return result;
    }

    // ═══════════════ CSV 导出 ═══════════════

    /// <summary>
    /// 导出 CSV 文件
    /// </summary>
    public void ExportCsv(string filePath, List<TrendChannel> channels, bool useTimeRange, DateTime? start, DateTime? end)
    {
        var filtered = channels.Select(ch => FilterSamples(ch, useTimeRange, start, end)).ToList();
        int maxLen = filtered.Count > 0 ? filtered.Max(f => f.Count) : 0;

        var sb = new StringBuilder(40 * channels.Count * Math.Min(maxLen, 10000));

        // 表头
        var headers = new List<string>(channels.Count * 2);
        foreach (var ch in channels)
        {
            headers.Add($"{ch.Name}_时间");
            headers.Add($"{ch.Name}_值");
        }
        sb.AppendLine(string.Join(",", headers));

        for (int i = 0; i < maxLen; i++)
        {
            var rowItems = new List<string>(channels.Count * 2);
            for (int c = 0; c < channels.Count; c++)
            {
                if (i < filtered[c].Count)
                {
                    var (time, val) = filtered[c][i];
                    rowItems.Add(time.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    rowItems.Add(val.ToString("F4"));
                }
                else
                {
                    rowItems.Add("");
                    rowItems.Add("");
                }
            }
            sb.AppendLine(string.Join(",", rowItems));
        }

        File.WriteAllText(filePath, "\uFEFF" + sb.ToString(), Encoding.UTF8);
    }

    // ═══════════════ PDF 导出 ═══════════════

    /// <summary>
    /// PDF 导出选项
    /// </summary>
    public class PdfExportOptions
    {
        public required string FilePath { get; init; }
        public required List<TrendChannel> Channels { get; init; }
        public bool UseTimeRange { get; init; }
        public DateTime? Start { get; init; }
        public DateTime? End { get; init; }
        public bool IncludeChart { get; init; }
        public bool IncludeData { get; init; }

        /// <summary>
        /// 报告头部的采样间隔文本（从 UI 传入）
        /// </summary>
        public string IntervalText { get; init; } = "";

        /// <summary>
        /// 报告头部的平均耗时文本（从 UI 传入）
        /// </summary>
        public string AvgTimeText { get; init; } = "";

        /// <summary>
        /// 图表区域截图 PNG 数据（从 UI 渲染后传入）
        /// </summary>
        public byte[]? ChartPngData { get; init; }
        public int ChartWidth { get; init; }
        public int ChartHeight { get; init; }
    }

    /// <summary>
    /// 导出 PDF 文件
    /// </summary>
    public void ExportPdf(PdfExportOptions options)
    {
        using var pdf = new SimplePdfWriter();

        // ============ 第一步：用 WPF 渲染中文标题区为图片 ============
        var headerVisual = CreatePdfHeaderVisual(options.Channels, options.UseTimeRange,
            options.Start, options.End, options.IntervalText, options.AvgTimeText);
        int hdrW = (int)headerVisual.ActualWidth;
        int hdrH = (int)headerVisual.ActualHeight;
        if (hdrW <= 0) hdrW = 800;
        if (hdrH <= 0) hdrH = 100;
        byte[] headerPng = RenderVisualToPng(headerVisual, hdrW, hdrH);
        double headerImgW = 495;
        double headerImgH = headerImgW * hdrH / hdrW;
        pdf.DrawImage(headerPng, 50, 30, headerImgW, headerImgH);
        double y = 30 + headerImgH + 10;

        // ============ 第二步：嵌入趋势图截图 ============
        if (options.IncludeChart && options.ChartPngData != null && options.ChartWidth > 0 && options.ChartHeight > 0)
        {
            double chartW = 495;
            double chartH = chartW * options.ChartHeight / options.ChartWidth;
            if (y + chartH > 800) chartH = 800 - y;
            pdf.DrawImage(options.ChartPngData, 50, y, chartW, chartH);
            y += chartH + 10;
        }

        // ============ 第三步：分页嵌入采样数据表格 ============
        if (options.IncludeData)
        {
            var filtered = options.Channels.Select(ch => FilterSamples(ch, options.UseTimeRange, options.Start, options.End)).ToList();
            int totalRows = filtered.Count > 0 ? filtered.Max(f => f.Count) : 0;
            if (totalRows == 0) goto pdfSave;

            // 动态计算每页可容纳的行数
            var probe = CreateDataTableVisual(options.Channels, filtered, 2, 0);
            int probeW = Math.Max((int)probe.ActualWidth, 800);
            int probeH = Math.Max((int)probe.ActualHeight, 40);
            double rowPixelHeight = probeH / 3.0;
            double headerPixelHeight = rowPixelHeight;

            double pdfTableWidth = 495.0;
            double scaleFactor = pdfTableWidth / probeW;
            double pageUsableHeight = pdf.PageHeight - 80 - 14;
            double firstPageRemain = pdf.PageHeight - y - 40 - 14;

            int calcRowsForHeight(double availablePt)
            {
                double availPixels = availablePt / scaleFactor - headerPixelHeight;
                return Math.Max(1, (int)(availPixels / rowPixelHeight));
            }

            int rowsFirstPage = calcRowsForHeight(firstPageRemain);
            int rowsPerFullPage = calcRowsForHeight(pageUsableHeight);
            int maxExportRows = Math.Min(totalRows, 5000);

            int offset = 0;
            int pageNum = 0;
            int totalPages = rowsFirstPage >= maxExportRows ? 1
                : 1 + ((maxExportRows - rowsFirstPage + rowsPerFullPage - 1) / rowsPerFullPage);

            while (offset < maxExportRows)
            {
                int rowsThisPage = (pageNum == 0) ? rowsFirstPage : rowsPerFullPage;
                int batchSize = Math.Min(rowsThisPage, maxExportRows - offset);

                if (pageNum > 0)
                    y = pdf.NewPage();

                var batchVisual = CreateDataTableVisual(options.Channels, filtered, batchSize, offset);
                int tblW = Math.Max((int)batchVisual.ActualWidth, 800);
                int tblH = Math.Max((int)batchVisual.ActualHeight, 50);
                byte[] tablePng = RenderVisualToPng(batchVisual, tblW, tblH);

                double tableW = pdfTableWidth;
                double tableH = tableW * tblH / tblW;

                pdf.DrawText($"Data {offset + 1}-{offset + batchSize} / {totalRows}  (Page {pageNum + 1}/{totalPages})",
                    50, y, 8, r: 0x75, g: 0x75, b: 0x75);
                y += 14;

                pdf.DrawImage(tablePng, 50, y, tableW, tableH);
                y += tableH + 10;

                offset += batchSize;
                pageNum++;
            }

            if (totalRows > maxExportRows)
            {
                pdf.DrawText($"... {totalRows - maxExportRows} more rows omitted. Export CSV for complete data.",
                    50, y, 8, r: 0x99, g: 0x99, b: 0x99);
            }
        }

    pdfSave:
        pdf.Save(options.FilePath);
    }

    // ═══════════════ 视觉元素创建 ═══════════════

    /// <summary>
    /// 创建 PDF 中文标题头部（用 WPF 渲染为图片）
    /// </summary>
    private StackPanel CreatePdfHeaderVisual(List<TrendChannel> channels, bool useTimeRange,
        DateTime? start, DateTime? end, string intervalText, string avgTimeText)
    {
        var panel = new StackPanel
        {
            Width = 800,
            Background = Brushes.White,
            Margin = new Thickness(10)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "📊 多变量趋势分析报告",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x79, 0x6B)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        string timeInfo = useTimeRange && start != null && end != null
            ? $"时间范围: {start:yyyy-MM-dd HH:mm:ss} ~ {end:yyyy-MM-dd HH:mm:ss}"
            : "时间范围: 全部数据";

        panel.Children.Add(new TextBlock
        {
            Text = $"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}    采样间隔: {intervalText}ms    {avgTimeText}",
            FontSize = 11,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 2)
        });

        panel.Children.Add(new TextBlock
        {
            Text = timeInfo,
            FontSize = 11,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 2)
        });

        string channelList = string.Join(", ", channels.Select(c => $"{c.Name}({c.DataType})"));
        panel.Children.Add(new TextBlock
        {
            Text = $"监控通道 ({channels.Count}): {channelList}",
            FontSize = 11,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = Brushes.LightGray,
            Margin = new Thickness(0, 4, 0, 4)
        });

        panel.Measure(new Size(800, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 800, panel.DesiredSize.Height));
        panel.UpdateLayout();

        return panel;
    }

    /// <summary>
    /// 创建数据表格视觉元素（用 WPF Grid 渲染），支持偏移量分页
    /// </summary>
    private FrameworkElement CreateDataTableVisual(List<TrendChannel> channels,
        List<List<(DateTime Time, double Value)>> filteredData, int maxRows, int offset = 0)
    {
        int cols = channels.Count * 2;
        int rows = maxRows + 1;

        var grid = new Grid { Width = Math.Max(800, cols * 130), Background = Brushes.White };

        for (int c = 0; c < cols; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < rows; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int c = 0; c < channels.Count; c++)
        {
            AddCell(grid, 0, c * 2, $"{channels[c].Name} 时间", true);
            AddCell(grid, 0, c * 2 + 1, $"{channels[c].Name} 值", true);
        }

        for (int r = 0; r < maxRows; r++)
        {
            int dataIdx = offset + r;
            for (int c = 0; c < channels.Count; c++)
            {
                if (dataIdx < filteredData[c].Count)
                {
                    var (time, val) = filteredData[c][dataIdx];
                    AddCell(grid, r + 1, c * 2, time.ToString("HH:mm:ss.fff"), false);
                    AddCell(grid, r + 1, c * 2 + 1, val.ToString(channels[c].DataType == PlcDataType.Real ? "F2" : "F0"), false);
                }
            }
        }

        grid.Measure(new Size(grid.Width, double.PositiveInfinity));
        grid.Arrange(new Rect(0, 0, grid.Width, grid.DesiredSize.Height));
        grid.UpdateLayout();

        return grid;
    }

    private static void AddCell(Grid grid, int row, int col, string text, bool isHeader)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = isHeader ? 10 : 9,
            FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
            Foreground = isHeader ? Brushes.White : Brushes.Black,
            Padding = new Thickness(3, 2, 3, 2),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var border = new Border
        {
            Child = tb,
            Background = isHeader
                ? new SolidColorBrush(Color.FromRgb(0x00, 0x79, 0x6B))
                : (row % 2 == 0 ? Brushes.WhiteSmoke : Brushes.White),
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(0, 0, 0.5, 0.5)
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        grid.Children.Add(border);
    }

    /// <summary>
    /// 将 WPF FrameworkElement 渲染为 PNG 字节数组（2x DPI 高清）
    /// </summary>
    public static byte[] RenderVisualToPng(FrameworkElement element, int width, int height)
    {
        if (width <= 0) width = 800;
        if (height <= 0) height = 100;

        int scale = 2;
        var rtb = new RenderTargetBitmap(width * scale, height * scale, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
