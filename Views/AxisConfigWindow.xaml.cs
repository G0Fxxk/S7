using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using S7WpfApp.Models;
using S7WpfApp.Services;

namespace S7WpfApp.Views;

/// <summary>
/// 轴地址配置窗口 — 每个地址可选择符号或输入绝对地址
/// </summary>
public partial class AxisConfigWindow : Window
{
    public AxisConfig? Result { get; private set; }

    private readonly ISymbolService _symbolService;
    private readonly AxisType _axisType;

    /// <summary>
    /// 地址行数据：标签、当前值（地址或符号名）、实际地址
    /// </summary>
    private class AddressRow
    {
        public string Label { get; set; } = "";
        public string DisplayText { get; set; } = "";  // 显示文本（符号名或绝对地址）
        public string Address { get; set; } = "";       // 实际 PLC 地址
        public TextBlock DisplayBlock { get; set; } = null!;
    }

    private readonly List<AddressRow> _rows = new();

    public AxisConfigWindow(string axisName, AxisType axisType, ISymbolService symbolService)
    {
        _symbolService = symbolService;
        _axisType = axisType;
        InitializeComponent();
        TitleText.Text = $"配置轴: {axisName}（{(axisType == AxisType.Rotary ? "旋转轴" : "线性轴")}）";
        BuildFields(null);
    }

    /// <summary>
    /// 编辑模式构造函数 — 预填充已有配置的地址
    /// </summary>
    public AxisConfigWindow(AxisConfig existingConfig, ISymbolService symbolService)
    {
        _symbolService = symbolService;
        _axisType = existingConfig.AxisType;
        InitializeComponent();
        TitleText.Text = $"编辑轴: {existingConfig.Name}（{(existingConfig.AxisType == AxisType.Rotary ? "旋转轴" : "线性轴")}）";
        BuildFields(existingConfig);
    }

    private void BuildFields(AxisConfig? existing)
    {
        // 定义地址字段及其在 AxisConfig 中对应的当前值
        var fields = new List<(string Label, string Key, string CurrentAddr)>
        {
            ("当前位置 (Real)", "CurrentPosition", existing?.CurrentPositionAddress ?? ""),
            ("伺服使能 (Bool)", "ServoEnable", existing?.ServoEnableAddress ?? ""),
            ("前进 (Bool)", "Forward", existing?.ForwardAddress ?? ""),
            ("后退 (Bool)", "Backward", existing?.BackwardAddress ?? ""),
            ("回原位 (Bool)", "Home", existing?.HomeAddress ?? ""),
            ("点动速度 (Real)", "JogSpeed", existing?.JogSpeedAddress ?? ""),
            ("手动速度 (Real)", "ManualSpeed", existing?.ManualSpeedAddress ?? ""),
            ("手动位置 (Real)", "ManualPosition", existing?.ManualPositionAddress ?? ""),
            ("手动触发 (Bool)", "ManualTrigger", existing?.ManualTriggerAddress ?? ""),
            ("原点 (Bool)", "Origin", existing?.OriginAddress ?? ""),
        };

        if (_axisType == AxisType.Linear)
        {
            fields.Add(("负限位 (Bool)", "NegativeLimit", existing?.NegativeLimitAddress ?? ""));
            fields.Add(("正限位 (Bool)", "PositiveLimit", existing?.PositiveLimitAddress ?? ""));
        }

        // 动态生成 UI 行
        foreach (var (label, key, currentAddr) in fields)
        {
            var row = new AddressRow { Label = label, Address = currentAddr, DisplayText = currentAddr };
            _rows.Add(row);

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
                CornerRadius = new CornerRadius(6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 标签
            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0x75, 0x75, 0x75)),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);

            // 地址显示（预填充已有值）
            var hasAddr = !string.IsNullOrEmpty(currentAddr);
            var displayBlock = new TextBlock
            {
                Text = hasAddr ? currentAddr : "未配置",
                Foreground = new SolidColorBrush(hasAddr
                    ? Color.FromRgb(0x33, 0x33, 0x33)
                    : Color.FromRgb(0x9E, 0x9E, 0x9E)),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(displayBlock, 1);
            grid.Children.Add(displayBlock);
            row.DisplayBlock = displayBlock;

            // 📋 符号选择按钮
            var symBtn = new Button
            {
                Content = "📋",
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 4, 0),
                Tag = row,
                ToolTip = "从符号表选择"
            };
            symBtn.Click += OnSymbolSelectClick;
            Grid.SetColumn(symBtn, 2);
            grid.Children.Add(symBtn);

            // ✏️ 手动输入按钮
            var manualBtn = new Button
            {
                Content = "✏️",
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 4, 0),
                Tag = row,
                ToolTip = "输入绝对地址"
            };
            manualBtn.Click += OnManualInputClick;
            Grid.SetColumn(manualBtn, 3);
            grid.Children.Add(manualBtn);

            // 🗑️ 删除按钮
            var delBtn = new Button
            {
                Content = "🗑️",
                Padding = new Thickness(8, 4, 8, 4),
                Tag = row,
                ToolTip = "清除地址"
            };
            delBtn.Click += OnDeleteClick;
            Grid.SetColumn(delBtn, 4);
            grid.Children.Add(delBtn);

            border.Child = grid;
            AddressPanel.Children.Add(border);
        }
    }

    /// <summary>
    /// 从符号表选择：分组 → 变量
    /// </summary>
    private async void OnSymbolSelectClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not AddressRow row) return;

        var categories = _symbolService.GetCategories().ToList();
        if (categories.Count == 0)
        {
            MessageBox.Show("暂无符号，请先在 DB 解析器中添加符号", "提示", MessageBoxButton.OK);
            return;
        }

        // 选择分组
        var catOptions = categories.Select(c => $"📂 {c}").ToArray();
        var selCat = await UIHelper.DisplayActionSheet("选择符号分组", "取消", null, catOptions);
        if (selCat == "取消" || string.IsNullOrEmpty(selCat)) return;

        var catName = selCat.Replace("📂 ", "");
        var symbols = _symbolService.GetSymbolsByCategory(catName).ToList();
        if (symbols.Count == 0) return;

        // 选择变量
        var symNames = symbols.Select(s => $"{s.Name} ({s.Address})").ToArray();
        var selSym = await UIHelper.DisplayActionSheet($"选择变量 - {catName}", "取消", null, symNames);
        if (selSym == "取消" || string.IsNullOrEmpty(selSym)) return;

        var sym = symbols.FirstOrDefault(s => selSym.StartsWith(s.Name));
        if (sym == null) return;

        // 显示符号名，保存实际地址
        row.DisplayText = sym.Name;
        row.Address = sym.Address;
        row.DisplayBlock.Text = sym.Name;
        row.DisplayBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x79, 0x6B));
    }

    /// <summary>
    /// 手动输入绝对地址
    /// </summary>
    private async void OnManualInputClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not AddressRow row) return;

        var addr = await UIHelper.DisplayPrompt(
            $"输入 {row.Label} 地址",
            "请输入绝对地址：",
            placeholder: "例如: DB200.DBD0",
            initialValue: row.Address);

        if (string.IsNullOrWhiteSpace(addr)) return;

        row.DisplayText = addr;
        row.Address = addr;
        row.DisplayBlock.Text = addr;
        row.DisplayBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
    }

    /// <summary>
    /// 删除/清除地址
    /// </summary>
    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not AddressRow row) return;

        row.DisplayText = "";
        row.Address = "";
        row.DisplayBlock.Text = "未配置";
        row.DisplayBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        // 按字段顺序读取地址
        int i = 0;
        var config = new AxisConfig
        {
            CurrentPositionAddress = _rows[i++].Address,
            ServoEnableAddress = _rows[i++].Address,
            ForwardAddress = _rows[i++].Address,
            BackwardAddress = _rows[i++].Address,
            HomeAddress = _rows[i++].Address,
            JogSpeedAddress = _rows[i++].Address,
            ManualSpeedAddress = _rows[i++].Address,
            ManualPositionAddress = _rows[i++].Address,
            ManualTriggerAddress = _rows[i++].Address,
            OriginAddress = _rows[i++].Address,
        };

        if (_axisType == AxisType.Linear && _rows.Count > i)
        {
            config.NegativeLimitAddress = _rows[i++].Address;
            config.PositiveLimitAddress = _rows[i++].Address;
        }

        Result = config;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
