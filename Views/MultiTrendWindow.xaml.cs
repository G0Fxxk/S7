using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using S7WpfApp.Models;
using S7WpfApp.Services;
using S7WpfApp.ViewModels;
using System.IO;
using System.Text;
using System.Windows.Controls;
using ScottPlot;
using ScottPlot.Plottables;
using SkiaSharp;

namespace S7WpfApp.Views;

/// <summary>
/// ScottPlot.WPF 版 多变量实时趋势监控窗口
/// 移植自 WinForms TrendChartManager + Form6 的全部功能
/// </summary>
public partial class MultiTrendWindow : Window
{
    private readonly MultiTrendViewModel _vm;

    // Sharp7 统一采样引擎
    private Services.Sharp7SamplingEngine? _samplingEngine;

    // 渲染相关
    private volatile bool _isRendering = false;

    private readonly Services.TrendExportService _exportService = new();
    private readonly TrendChartManager _chartManager;

    public MultiTrendWindow(MultiTrendViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        _chartManager = new TrendChartManager(
            PlotsPanel,
            PlotsScrollViewer,
            _vm.Channels,
            val => WindowTimeText.Text = $"{val:F1} s",
            val => AvgTimeText.Text = val
        );

        ChannelsGrid.ItemsSource = _vm.Channels;

        _vm.Channels.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (TrendChannel ch in e.NewItems)
                    ch.PropertyChanged += OnChannelPropertyChanged;
            }
            if (e.OldItems != null)
            {
                foreach (TrendChannel ch in e.OldItems)
                    ch.PropertyChanged -= OnChannelPropertyChanged;
            }
            _chartManager.SyncPlots();
        };

        WindowTimeText.Text = $"{_chartManager.ViewWindowSeconds:F1} s";
    }

    private void OnChannelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrendChannel.IsSelected))
        {
            _chartManager.SyncPlots();
        }
    }

    private void OnPlotsScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _chartManager.UpdatePlotHeights();
    }

    // ═══════════════ 工具栏按钮事件 ═══════════════

    private void OnToggleCursorClick(object sender, RoutedEventArgs e)
    {
        _chartManager.ToggleCursors();
    }

    private void OnTogglePauseClick(object sender, RoutedEventArgs e)
    {
        _chartManager.IsAutoScroll = !_chartManager.IsAutoScroll;
        _chartManager.SetPlotsInteraction(_chartManager.IsAutoScroll);
        PauseBtn.Content = _chartManager.IsAutoScroll ? "⏸ 暂停" : "▶ 继续";
        _chartManager.RefreshAllPlots();
    }

    private void OnAutoScaleClick(object sender, RoutedEventArgs e)
    {
        _chartManager.AutoScaleY();
    }

    private void OnResetViewClick(object sender, RoutedEventArgs e)
    {
        _chartManager.ResetView();
        PauseBtn.Content = "⏸ 暂停";
    }

    private void OnZoomInTimeClick(object sender, RoutedEventArgs e)
    {
        _chartManager.ZoomTimeWindow(0.8, _vm.SamplingStartTime);
    }

    private void OnZoomOutTimeClick(object sender, RoutedEventArgs e)
    {
        _chartManager.ZoomTimeWindow(1.25, _vm.SamplingStartTime);
    }

    private void CapturePlotsPanel(string? savePath = null)
    {
        // 确保 UI 刷新
        PlotsPanel.UpdateLayout();
        int w = (int)PlotsPanel.ActualWidth;
        int h = (int)PlotsPanel.ActualHeight;
        if (w == 0 || h == 0) return;

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(PlotsPanel);

        if (savePath != null)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.OpenWrite(savePath);
            encoder.Save(fs);
            MessageBox.Show($"图片已保存至: {savePath}");
        }
        else
        {
            Clipboard.SetImage(rtb);
            MessageBox.Show("图表已复制到剪贴板");
        }
    }

    private void OnSaveImageClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG Image|*.png",
            FileName = $"TrendChart_{DateTime.Now:yyyyMMdd_HHmmss}"
        };
        if (dlg.ShowDialog() == true)
        {
            CapturePlotsPanel(dlg.FileName);
        }
    }

    private void OnCopyImageClick(object sender, RoutedEventArgs e)
    {
        try
        {
            CapturePlotsPanel();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"复制失败: {ex.Message}");
        }
    }


    // ═══════════════ 频道管理 ═══════════════

    private void OnSelectSymbolClick(object sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        if (btn == null) return;

        var menu = new ContextMenu();
        var categories = _vm.SymbolService.GetCategories().ToList();

        foreach (var cat in categories)
        {
            var catItem = new MenuItem { Header = string.IsNullOrEmpty(cat) ? "未分类" : $"📂 {cat}" };
            var symbols = _vm.SymbolService.GetSymbolsByCategory(cat).ToList();

            foreach (var sym in symbols)
            {
                var symItem = new MenuItem { Header = $"{sym.Name} ({sym.DataType}) - {sym.Address}" };
                symItem.Click += (s, ev) =>
                {
                    NameBox.Text = sym.Name;
                    AddressBox.Text = sym.Address;
                };
                catItem.Items.Add(symItem);
            }

            if (catItem.Items.Count > 0)
            {
                menu.Items.Add(catItem);
            }
        }

        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "没有任何符号", IsEnabled = false });
        }

        btn.ContextMenu = menu;
        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }

    private void OnAddChannelClick(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        string address = AddressBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) name = address;

        var error = _vm.AddChannel(name, address);
        if (error != null)
            MessageBox.Show(error, "提示");
    }

    public void AddChannel(string name, string address)
    {
        var error = _vm.AddChannel(name, address);
        if (error != null)
            MessageBox.Show(error, "提示");
    }

    private void OnDeleteChannelClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement ele && ele.Tag is TrendChannel channel)
        {
            var error = _vm.RemoveChannel(channel);
            if (error != null)
                MessageBox.Show(error, "提示");
        }
    }

    private void OnClearChannelsClick(object sender, RoutedEventArgs e)
    {
        if (_vm.ClearChannels())
            _chartManager.ClearPlots();
    }

    private void OnClearChartClick(object sender, RoutedEventArgs e)
    {
        _vm.ClearData();
        _chartManager.RefreshAllPlots();
    }

    // ═══════════════ 颜色选择 ═══════════════

    private void OnColorBlockClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TrendChannel channel)
        {
            var palette = _vm.GetPalette();
            var menu = new ContextMenu();
            var names = new[] { "青色 (Cyan)", "亮红 (Bright Red)", "亮绿 (Bright Green)", "橙黄 (Amber)", "亮蓝 (Bright Blue)", "紫色 (Purple)", "橘色 (Orange)" };
            for (int i = 0; i < palette.Length; i++)
            {
                var mi = new MenuItem { Header = names[i] };
                var c = palette[i];
                var iconBorder = new Border
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B)),
                    Width = 16,
                    Height = 16,
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1)
                };
                mi.Icon = iconBorder;
                mi.Click += (s, ev) =>
                {
                    channel.LinePen = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B)), 1.5);
                    channel.LinePen.Freeze();
                    if (channel.PlotTrace != null)
                        channel.PlotTrace.Color = ScottPlot.Color.FromColor(c);
                    ChannelsGrid.Items.Refresh();
                    _chartManager.RefreshAllPlots();
                };
                menu.Items.Add(mi);
            }
            btn.ContextMenu = menu;
            menu.IsOpen = true;
        }
    }

    // ═══════════════ Sharp7 采样引擎 (完全保留) ═══════════════

    private void OnStartStopClick(object sender, RoutedEventArgs e)
    {
        if (_vm.IsRunning)
        {
            _samplingEngine?.Stop();
            _samplingEngine?.Dispose();
            _samplingEngine = null;

            _vm.StopSampling();
            StartStopBtn.Content = "▶ 开始采样";
        }
        else
        {
            _vm.ApplyInterval(IntervalBox.Text);
            var samplingChannels = _vm.StartSampling();
            if (samplingChannels == null)
            {
                MessageBox.Show("请先在左侧输入 PLC 地址并添加监控通道。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            StartDedicatedSampling(samplingChannels);
            StartStopBtn.Content = "⏸ 暂停采样";
        }
    }

    private void ApplyInterval()
    {
        _vm.ApplyInterval(IntervalBox.Text);
    }

    private void OnIntervalKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyInterval();
    }

    private void StartDedicatedSampling(List<Services.SamplingChannel> samplingChannels)
    {
        _samplingEngine = new Services.Sharp7SamplingEngine(_vm.PlcService)
        {
            IntervalMs = _vm.IntervalMs,
            OnUiRefreshNeeded = () =>
            {
                if (!_isRendering)
                {
                    _isRendering = true;
                    _ = Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            if (!_chartManager.IsCursorsVisible)
                            {
                                _vm.UpdateStats();
                                AvgTimeText.Text = _vm.AvgTimeText;
                                SampleCountText.Text = _vm.SampleCountText;
                                MemoryText.Text = _vm.MemoryText;
                            }

                            if (_chartManager.IsAutoScroll)
                            {
                                _chartManager.UpdateSlidingWindow(_vm.SamplingStartTime);
                            }
                            _chartManager.RefreshAllPlots();
                        }
                        finally
                        {
                            _isRendering = false;
                        }
                    }, System.Windows.Threading.DispatcherPriority.Render);
                }
            },
            OnError = msg =>
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(msg, "错误");
                    _vm.StopSampling();
                    StartStopBtn.Content = "▶ 开始采样";
                });
            }
        };

        _samplingEngine.Start(samplingChannels);
    }

    // ═══════════════ 导出功能 ═══════════════

    private void OnExportReportClick(object sender, RoutedEventArgs e)
    {
        if (_vm.Channels.Count == 0)
        {
            MessageBox.Show("没有可导出的通道数据。", "提示");
            return;
        }

        // 获取采样全局时间范围
        DateTime? firstTime = null, lastTime = null;
        foreach (var ch in _vm.Channels)
        {
            if (ch.AllSamples.Count > 0)
            {
                var ft = ch.AllSamples[0].Time;
                var lt = ch.AllSamples[^1].Time;
                if (firstTime == null || ft < firstTime) firstTime = ft;
                if (lastTime == null || lt > lastTime) lastTime = lt;
            }
        }

        var dlg = new TrendExportDialog(_vm.Channels.Select(c => $"{c.Name} ({c.Address})"), firstTime, lastTime);
        dlg.Owner = this;
        if (dlg.ShowDialog() != true) return;

        var selectedChannels = dlg.SelectedChannelIndices
            .Where(i => i < _vm.Channels.Count)
            .Select(i => _vm.Channels[i])
            .ToList();

        string filter = dlg.ExportFormat == "pdf" ? "PDF 文件|*.pdf" : "CSV 数据|*.csv";
        string ext = dlg.ExportFormat == "pdf" ? ".pdf" : ".csv";
        var saveDlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            DefaultExt = ext,
            FileName = $"趋势报告_{DateTime.Now:yyyyMMdd_HHmmss}"
        };

        if (saveDlg.ShowDialog() != true) return;

        try
        {
            if (dlg.ExportFormat == "pdf")
            {
                // 截图图表区域（在 UI 线程上完成）
                byte[]? chartPng = null;
                int chartW = 0, chartH = 0;
                if (dlg.IncludeChart)
                {
                    try
                    {
                        PlotsPanel.UpdateLayout();
                        chartW = (int)PlotsPanel.ActualWidth;
                        chartH = (int)PlotsPanel.ActualHeight;
                        if (chartW > 0 && chartH > 0)
                            chartPng = TrendExportService.RenderVisualToPng(PlotsPanel, chartW, chartH);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"PDF 图表截图失败: {ex.Message}");
                    }
                }

                _exportService.ExportPdf(new TrendExportService.PdfExportOptions
                {
                    FilePath = saveDlg.FileName,
                    Channels = selectedChannels,
                    UseTimeRange = dlg.UseTimeRange,
                    Start = dlg.ExportStartTime,
                    End = dlg.ExportEndTime,
                    IncludeChart = dlg.IncludeChart,
                    IncludeData = dlg.IncludeData,
                    IntervalText = IntervalBox.Text,
                    AvgTimeText = AvgTimeText.Text,
                    ChartPngData = chartPng,
                    ChartWidth = chartW,
                    ChartHeight = chartH
                });
            }
            else
            {
                _exportService.ExportCsv(saveDlg.FileName, selectedChannels,
                    dlg.UseTimeRange, dlg.ExportStartTime, dlg.ExportEndTime);
            }
            MessageBox.Show("导出成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出时发生错误：\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
