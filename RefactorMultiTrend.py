import re

with open('Views/MultiTrendWindow.xaml.cs', 'r', encoding='utf-8') as f:
    code = f.read()

# 1. 变量移除和添加 _chartManager
code = re.sub(
    r'(    private readonly Services\.TrendExportService _exportService = new\(\);\n\n    // --- ScottPlot 游标 ---\n    private bool _isCursorsVisible = false;\n    private bool _isDragging = false;\n    private VerticalLine\? _draggingLine = null;\n\n    // --- 滚动控制 ---\n    private bool _isAutoScroll = true;\n    private double _viewWindowSeconds = 60;\n\n    // --- 按数据类型分组的图表字典（增量更新缓存） ---\n    private readonly Dictionary<PlcDataType, ScottPlot\.WPF\.WpfPlot> _typePlots = new\(\);)',
    r'    private readonly Services.TrendExportService _exportService = new();\n    private readonly TrendChartManager _chartManager;',
    code
)

# 2. 构造函数
old_ctor = """    public MultiTrendWindow(IPlcService plcService, Services.ISymbolService symbolService)
    {
        InitializeComponent();
        _plcService = plcService;
        _symbolService = symbolService;
        ChannelsGrid.ItemsSource = Channels;

        Channels.CollectionChanged += (s, e) =>
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
            SyncPlots();
        };

        UpdateWindowTimeText();
    }"""
new_ctor = """    public MultiTrendWindow(IPlcService plcService, Services.ISymbolService symbolService)
    {
        InitializeComponent();
        _plcService = plcService;
        _symbolService = symbolService;
        
        _chartManager = new TrendChartManager(
            PlotsPanel, 
            PlotsScrollViewer, 
            Channels,
            val => WindowTimeText.Text = $"{val:F1} s",
            val => AvgTimeText.Text = val
        );

        ChannelsGrid.ItemsSource = Channels;

        Channels.CollectionChanged += (s, e) =>
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
    }"""
code = code.replace(old_ctor, new_ctor)

# 3. 属性改变
code = code.replace("SyncPlots();", "_chartManager.SyncPlots();", 1)

# 4. 删除从 SyncPlots 到 Theme 这个超级大段 (因为太长，直接用切片大法移除)
start_idx = code.find('    /// <summary>\n    /// 增量式按数据类型分组同步图表。')
end_idx = code.find('    // ═══════════════ 工具栏按钮事件 ═══════════════')
if start_idx > 0 and end_idx > start_idx:
    code = code[:start_idx] + """    private void OnPlotsScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _chartManager.UpdatePlotHeights();
    }

""" + code[end_idx:]

# 5. 替换工具栏交互
orig_interaction = """    private void OnToggleCursorClick(object sender, RoutedEventArgs e)
    {
        ToggleCursors();
    }

    private void OnTogglePauseClick(object sender, RoutedEventArgs e)
    {
        _isAutoScroll = !_isAutoScroll;
        SetPlotsInteraction(_isAutoScroll);
        PauseBtn.Content = _isAutoScroll ? "⏸ 暂停" : "▶ 继续";
        RefreshAllPlots();
    }

    private void OnAutoScaleClick(object sender, RoutedEventArgs e)
    {
        foreach (var kv in _typePlots)
        {
            if (kv.Key != PlcDataType.Bool)
                kv.Value.Plot.Axes.AutoScaleY();
            if (!_isAutoScroll)
                kv.Value.Plot.Axes.AutoScaleX();
        }
        RefreshAllPlots();
    }

    private void OnResetViewClick(object sender, RoutedEventArgs e)
    {
        _isAutoScroll = true;
        _viewWindowSeconds = 60.0;
        UpdateWindowTimeText();

        SetPlotsInteraction(true);
        PauseBtn.Content = "⏸ 暂停";

        foreach (var kv in _typePlots)
        {
            if (kv.Key != PlcDataType.Bool)
                kv.Value.Plot.Axes.AutoScaleY();
        }
        RefreshAllPlots();
    }

    private void UpdateWindowTimeText()
    {
        WindowTimeText.Text = $"{_viewWindowSeconds:F1} s";
    }

    private void OnZoomInTimeClick(object sender, RoutedEventArgs e)
    {
        // 缩小范围 -> 放大细节
        ZoomTimeWindow(0.8);
    }

    private void OnZoomOutTimeClick(object sender, RoutedEventArgs e)
    {
        // 扩大范围 -> 缩小细节拉长尺度
        ZoomTimeWindow(1.25);
    }

    private void OnPlotMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isAutoScroll)
        {
            // e.Delta > 0 向上滚代表放大 (Zoom-In)
            // e.Delta < 0 向下滚代表缩小 (Zoom-Out)
            double multiplier = e.Delta > 0 ? 0.8 : 1.25;
            ZoomTimeWindow(multiplier);

            // 标记已处理阻止默认行为 (仅在自动滚动时，暂停时仍由内置交互处理)
            e.Handled = true;
        }
    }

    private void ZoomTimeWindow(double multiplier)
    {
        _viewWindowSeconds = Math.Clamp(_viewWindowSeconds * multiplier, 1.0, 3600.0);
        UpdateWindowTimeText();
        if (_isAutoScroll)
        {
            UpdateSlidingWindow();
            RefreshAllPlots();
        }
    }"""
new_interaction = """    private void OnToggleCursorClick(object sender, RoutedEventArgs e)
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
        _chartManager.ZoomTimeWindow(0.8, _samplingStartTime);
    }

    private void OnZoomOutTimeClick(object sender, RoutedEventArgs e)
    {
        _chartManager.ZoomTimeWindow(1.25, _samplingStartTime);
    }"""
code = code.replace(orig_interaction, new_interaction)

# 6. 删除游标和大量鼠标事件 (找到开端，往下切掉直到频道管理)
c_start = code.find('    // ══════════════\xef\xbf\xbd游标系统 ══════════════\xef\xbf\xbd')
if c_start < 0:
    c_start = code.find('    // ══════════════?游标系统 ══════════════?') # encoding quirks...
if c_start < 0: # Let's search by string togglecursors
    c_start = code.find('    private void ToggleCursors()')
    # Backup a bit to catch the comment
    c_start = code.rfind('//', 0, c_start)
    if c_start > 0:
        c_start = code.rfind('   ', 0, c_start)

c_end = code.find('    // ═══════════════ 频道管理 ═══════════════')
if c_start > 0 and c_end > c_start:
    code = code[:c_start] + "\n" + code[c_end:]

# 7. 更新引用
code = code.replace("_typePlots.Clear();", "_chartManager.ClearPlots();")
code = code.replace("PlotsPanel.Children.Clear();", "")
code = code.replace("RefreshAllPlots();", "_chartManager.RefreshAllPlots();")
code = code.replace("if (!_isCursorsVisible)", "if (!_chartManager.IsCursorsVisible)")
code = code.replace("if (_isAutoScroll)", "if (_chartManager.IsAutoScroll)")
code = code.replace("UpdateSlidingWindow();", "_chartManager.UpdateSlidingWindow(_samplingStartTime);")

# 8. 删除原始 UpdateSlidingWindow
s_win_start = code.find('    /// <summary>\n    /// 自动滚动窗口')
s_win_end = code.find('    // ═══════════════ 导出功能 ═══════════════')
if s_win_start > 0 and s_win_end > s_win_start:
    code = code[:s_win_start] + code[s_win_end:]

with open('Views/MultiTrendWindow.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(code)
