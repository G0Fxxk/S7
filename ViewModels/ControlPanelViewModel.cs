using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using S7WpfApp.Models;
using S7WpfApp.Services;

namespace S7WpfApp.ViewModels;

/// <summary>
/// 控制面板 ViewModel
/// </summary>
public partial class ControlPanelViewModel : BaseViewModel
{
    private readonly IPlcService _plcService;
    private readonly IBindingService _bindingService;
    private readonly IAxisConfigService _axisConfigService;
    private readonly IAuthService _authService;
    private readonly ISymbolService _symbolService;
    private readonly IControlPanelConfigService _configService;
    private System.Timers.Timer? _refreshTimer;

    public ObservableCollection<ControlGroup> Groups { get; } = new();
    public ObservableCollection<Models.AxisConfig> AxisConfigs { get; } = new();

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private long _latency;

    public ControlPanelViewModel(IPlcService plcService, IBindingService bindingService, IAxisConfigService axisConfigService, IAuthService authService, ISymbolService symbolService, IControlPanelConfigService configService)
    {
        _plcService = plcService;
        _bindingService = bindingService;
        _axisConfigService = axisConfigService;
        _authService = authService;
        _symbolService = symbolService;
        _configService = configService;
        Title = "控制面板";

        _plcService.ConnectionStatusChanged += (s, connected) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                IsConnected = connected;
                if (connected)
                {
                    StartRefresh();
                }
                else
                {
                    StopRefresh();
                }
            });
        };

        _plcService.LatencyUpdated += (s, latency) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => Latency = latency);
        };

        _bindingService.BindingsChanged += (s, e) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => LoadBindings());
        };

        // 订阅符号表更新事件
        _symbolService.SymbolTableChanged += (s, e) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => RefreshControlAddresses());
        };

        // 订阅趋势采样独占模式（暂停/恢复刷新）
        _plcService.RefreshPauseRequested += (s, paused) =>
        {
            if (paused)
                StopRefresh();
            else if (IsConnected)
                StartRefresh();
        };

        IsConnected = _plcService.IsConnected;
        LoadBindings();

        if (IsConnected)
        {
            StartRefresh();
        }
    }

    /// <summary>
    /// 刷新控件地址（当符号表更新时调用）
    /// </summary>
    private void RefreshControlAddresses()
    {
        int updatedCount = 0;

        foreach (var group in Groups)
        {
            foreach (var control in group.Controls)
            {
                if (!string.IsNullOrEmpty(control.Binding.SymbolName))
                {
                    var newAddress = _symbolService.GetAddress(control.Binding.SymbolName);
                    if (newAddress != null && newAddress != control.Binding.Address)
                    {
                        control.Binding.Address = newAddress;
                        updatedCount++;
                    }
                }
            }
        }

        if (updatedCount > 0)
        {
            // 保存更新后的绑定（逐个保存）
            foreach (var group in Groups)
            {
                foreach (var control in group.Controls)
                {
                    if (!string.IsNullOrEmpty(control.Binding.SymbolName))
                    {
                        _bindingService.SaveBinding(control.Binding);
                    }
                }
            }
            System.Diagnostics.Debug.WriteLine($"控件地址更新: {updatedCount} 个");
        }
    }

    /// <summary>
    /// 加载绑定配置
    /// </summary>
    [RelayCommand]
    private void LoadBindings()
    {
        Groups.Clear();
        AxisConfigs.Clear();

        // 加载轴配置
        foreach (var axis in _axisConfigService.GetAll())
        {
            AxisConfigs.Add(axis);
        }

        var bindings = _bindingService.GetBindings()
            .Where(b => b.Type != BindingType.None)
            .OrderBy(b => b.Order);

        // 按控件类型分组
        var typeNames = new Dictionary<BindingType, string>
        {
            { BindingType.MomentaryButton, "👆 点动按钮" },
            { BindingType.ToggleButton, "🔘 保持按钮" },
            { BindingType.NumericInput, "✏️ 数值输入" },
            { BindingType.StatusIndicator, "📊 状态指示" }
        };

        var groupDict = new Dictionary<BindingType, ControlGroup>();

        foreach (var binding in bindings)
        {
            if (!groupDict.TryGetValue(binding.Type, out var group))
            {
                var name = typeNames.GetValueOrDefault(binding.Type, $"⚙️ {binding.Type}");
                group = new ControlGroup { Name = name };
                groupDict[binding.Type] = group;
                Groups.Add(group);
            }

            group.Controls.Add(new ControlItem { Binding = binding });
        }

        // 将轴配置也添加为控件组显示
        if (AxisConfigs.Count > 0)
        {
            var axisGroup = new ControlGroup { Name = "🔧 轴控制" };
            foreach (var axis in AxisConfigs)
            {
                axisGroup.Controls.Add(new ControlItem
                {
                    Binding = new ControlBinding
                    {
                        Name = $"{axis.Name}",
                        Address = axis.CurrentPositionAddress,
                        DataType = "AxisControl",
                        Type = BindingType.StatusIndicator,
                        Group = axisGroup.Name,
                        SymbolName = axis.Id
                    }
                });
            }
            Groups.Add(axisGroup);
        }
    }

    private void StartRefresh()
    {
        StopRefresh();
        _refreshTimer = new System.Timers.Timer(100);  // 100ms 刷新间隔
        _refreshTimer.Elapsed += OnRefreshTimerElapsed;
        _refreshTimer.AutoReset = false;  // 手动重启，避免重叠执行
        _refreshTimer.Start();
    }

    private void StopRefresh()
    {
        if (_refreshTimer != null)
        {
            _refreshTimer.Elapsed -= OnRefreshTimerElapsed;
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _refreshTimer = null;
        }
    }

    private async void OnRefreshTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        await RefreshValuesAsync();

        // 完成后重新启动定时器
        _refreshTimer?.Start();
    }

    private async Task RefreshValuesAsync()
    {
        if (!IsConnected || IsRefreshing) return;

        IsRefreshing = true;

        try
        {
            // 复制集合以避免并发修改
            var groups = Groups.ToList();

            foreach (var group in groups)
            {
                var controls = group.Controls.ToList();
                foreach (var control in controls)
                {
                    if (!IsConnected) break;  // 断开连接时停止

                    try
                    {
                        var value = await ReadValueAsync(control.Binding);
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            control.CurrentValue = value;
                        });
                    }
                    catch
                    {
                        // 忽略单个读取错误
                    }
                }
            }
        }
        catch
        {
            // 忽略整体错误
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task<object?> ReadValueAsync(ControlBinding binding)
    {
        return await _configService.ReadValueAsync(binding);
    }

    private async Task WriteTypedAsync(ControlBinding binding, object value)
    {
        await _configService.WriteTypedAsync(binding, value);
    }

    /// <summary>
    /// 点动按钮按下
    /// </summary>
    [RelayCommand]
    private async Task MomentaryPressAsync(ControlItem? item)
    {
        if (item == null || !IsConnected) return;

        try
        {
            await WriteTypedAsync(item.Binding, true);
            item.IsPressed = true;
        }
        catch (Exception ex)
        {
            SetError($"写入失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 点动按钮松开
    /// </summary>
    [RelayCommand]
    private async Task MomentaryReleaseAsync(ControlItem? item)
    {
        if (item == null || !IsConnected) return;

        try
        {
            await WriteTypedAsync(item.Binding, false);
            item.IsPressed = false;
        }
        catch (Exception ex)
        {
            SetError($"写入失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 切换按钮
    /// </summary>
    [RelayCommand]
    private async Task ToggleAsync(ControlItem? item)
    {
        if (item == null || !IsConnected) return;

        try
        {
            var currentValue = item.CurrentValue is bool b ? b : (item.CurrentValue?.ToString()?.ToLower() == "true");
            await WriteTypedAsync(item.Binding, !currentValue);
        }
        catch (Exception ex)
        {
            SetError($"写入失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 写入数值

    /// </summary>
    [RelayCommand]
    private async Task WriteNumericAsync(ControlItem? item)
    {
        if (item == null || !IsConnected || string.IsNullOrWhiteSpace(item.InputValue)) return;

        try
        {
            var dt = item.Binding.DataType?.ToLower() ?? "";

            if (dt == "char" || dt == "string")
            {
                // 字符/字符串类型直接传入文本
                await WriteTypedAsync(item.Binding, item.InputValue);
            }
            else
            {
                // 数字类型强制解析
                if (float.TryParse(item.InputValue, out var numVal))
                {
                    if (dt == "int" || dt == "dint" || dt == "byte")
                    {
                        await WriteTypedAsync(item.Binding, Math.Round(numVal));
                    }
                    else
                    {
                        await WriteTypedAsync(item.Binding, numVal);
                    }
                }
                else
                {
                    await WriteTypedAsync(item.Binding, item.InputValue);
                }
            }
        }
        catch (Exception ex)
        {
            SetError($"写入失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 导出配置（含控件绑定 + 轴配置 + 符号表）
    /// </summary>
    [RelayCommand]
    private async Task ExportConfigAsync()
    {
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出控制面板配置",
                Filter = "JSON 文件|*.json",
                FileName = $"config_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                DefaultExt = ".json"
            };

            if (dlg.ShowDialog() != true) return;

            var (json, bindingCount, axisCount, symCount) = _configService.BuildExportConfig();
            await System.IO.File.WriteAllTextAsync(dlg.FileName, json);

            await UIHelper.DisplayAlert("导出成功",
                $"控件绑定: {bindingCount} 个\n" +
                $"轴配置: {axisCount} 个\n" +
                $"符号: {symCount} 个\n\n" +
                $"文件路径: {dlg.FileName}", "确定");
        }
        catch (Exception ex)
        {
            await UIHelper.DisplayAlert("错误", ex.Message, "确定");
        }
    }

    /// <summary>
    /// 导入配置（含控件绑定 + 轴配置 + 符号表）
    /// </summary>
    [RelayCommand]
    private async Task ImportConfigAsync()
    {
        try
        {
            var pickedPath = UIHelper.PickFile("选择配置文件", "JSON 文件|*.json|所有文件|*.*");
            if (pickedPath == null) return;

            var (bindings, axes, symbols) = await _configService.ImportConfigAsync(pickedPath);
            LoadBindings();

            await UIHelper.DisplayAlert("导入成功",
                $"控件绑定: {bindings} 个\n" +
                $"轴配置: {axes} 个\n" +
                $"符号: {symbols} 个（跳过已存在的）", "确定");
        }
        catch (Exception ex)
        {
            await UIHelper.DisplayAlert("错误", ex.Message, "确定");
        }
    }

    /// <summary>
    /// 绑定类型选项
    /// </summary>
    public List<string> BindingTypeOptions { get; } = new()
    {
        "点动按钮",
        "保持按钮",
        "数值输入",
        "状态指示"
    };

    /// <summary>
    /// 数据类型选项
    /// </summary>
    public List<string> DataTypeOptions { get; } = new()
    {
        "Bool",
        "Byte",
        "Char",
        "Int",
        "DInt",
        "Real",
        "String"
    };

    /// <summary>
    /// 手动添加控件（编排方法）
    /// </summary>
    [RelayCommand]
    private async Task AddControlAsync()
    {
        // 1. 选择地址
        var (address, symbolName) = await SelectAddressAsync();
        if (string.IsNullOrWhiteSpace(address)) return;

        // 2. 选择控件类型和数据类型
        var (bindingType, dataType) = await SelectBindingTypeAsync();
        if (bindingType == BindingType.None) return;

        // 3. String 类型需要额外输入最大长度
        int stringMaxLength = 254;
        if (dataType == "String")
        {
            var lenStr = await UIHelper.DisplayPrompt(
                "字符串长度", "请输入 S7 String 最大长度（默认 254）：",
                placeholder: "254", initialValue: "254");
            if (int.TryParse(lenStr, out int len) && len > 0 && len <= 254)
                stringMaxLength = len;
        }

        // 4. 输入名称
        var defaultName = symbolName ?? address;
        var name = await UIHelper.DisplayPrompt(
            "控件名称", "请输入控件显示名称：",
            placeholder: "例如: A轴点动", initialValue: defaultName);
        if (string.IsNullOrWhiteSpace(name)) name = defaultName;

        // 5. 输入分组
        var group = await UIHelper.DisplayPrompt(
            "分组", "请输入分组名称：", initialValue: "默认");
        if (string.IsNullOrWhiteSpace(group)) group = "默认";

        // 6. 创建并保存绑定
        CreateAndSaveBinding(address, name, group, bindingType, dataType, stringMaxLength);
        await UIHelper.DisplayAlert("成功", $"已添加控件 \"{name}\"", "确定");
    }

    /// <summary>
    /// 选择地址：从符号浏览器或手动输入
    /// </summary>
    private async Task<(string? Address, string? SymbolName)> SelectAddressAsync()
    {
        var inputMethod = await UIHelper.DisplayActionSheet(
            "选择输入方式", "取消", null,
            "📋 浏览符号变量", "✏️ 手动输入地址");

        if (inputMethod == "取消" || string.IsNullOrEmpty(inputMethod))
            return (null, null);

        if (inputMethod == "📋 浏览符号变量")
        {
            var categories = _symbolService.GetCategories().ToList();
            if (categories.Count == 0)
            {
                await UIHelper.DisplayAlert("提示", "暂无符号变量，请先在 DB 解析器中添加符号", "确定");
                return (null, null);
            }

            var catOptions = categories.Select(c => $"📂 {c}").ToArray();
            var selectedCat = await UIHelper.DisplayActionSheet("选择符号分组", "取消", null, catOptions);
            if (selectedCat == "取消" || string.IsNullOrEmpty(selectedCat)) return (null, null);

            var catName = selectedCat.Replace("📂 ", "");
            var symbols = _symbolService.GetSymbolsByCategory(catName).ToList();
            if (symbols.Count == 0)
            {
                await UIHelper.DisplayAlert("提示", "该分组下没有符号", "确定");
                return (null, null);
            }

            var symbolNames = symbols.Select(s => $"{s.Name} ({s.Address})").ToArray();
            var selected = await UIHelper.DisplayActionSheet($"选择变量 - {catName}", "取消", null, symbolNames);
            if (selected == "取消" || string.IsNullOrEmpty(selected)) return (null, null);

            var selectedSymbol = symbols.FirstOrDefault(s => selected.StartsWith(s.Name));
            if (selectedSymbol == null) return (null, null);

            return (selectedSymbol.Address, selectedSymbol.Name);
        }
        else
        {
            var address = await UIHelper.DisplayPrompt(
                "添加控件", "请输入 PLC 地址：",
                placeholder: "例如: DB200.DBX0.0");
            return (address, null);
        }
    }

    /// <summary>
    /// 选择控件类型和数据类型
    /// </summary>
    private async Task<(BindingType Type, string DataType)> SelectBindingTypeAsync()
    {
        var typeAction = await UIHelper.DisplayActionSheet(
            "选择控件类型", "取消", null,
            BindingTypeOptions.ToArray());

        if (typeAction == "取消" || string.IsNullOrEmpty(typeAction))
            return (BindingType.None, "Bool");

        var bindingType = typeAction switch
        {
            "点动按钮" => BindingType.MomentaryButton,
            "保持按钮" => BindingType.ToggleButton,
            "数值输入" => BindingType.NumericInput,
            "状态指示" => BindingType.StatusIndicator,
            _ => BindingType.None
        };

        // 非布尔类型需要选择数据类型
        var dataType = "Bool";
        if (bindingType == BindingType.NumericInput || bindingType == BindingType.StatusIndicator)
        {
            var dataTypeAction = await UIHelper.DisplayActionSheet(
                "选择数据类型", "取消", null,
                DataTypeOptions.ToArray());

            if (dataTypeAction == "取消" || string.IsNullOrEmpty(dataTypeAction))
                return (BindingType.None, "Bool");
            dataType = dataTypeAction;
        }

        return (bindingType, dataType);
    }

    /// <summary>
    /// 解析 DB 编号、关联符号、创建并保存绑定配置
    /// </summary>
    private void CreateAndSaveBinding(string address, string name, string group, BindingType bindingType, string dataType, int stringMaxLength = 254)
    {
        int dbNumber = 0;
        if (address.StartsWith("DB", StringComparison.OrdinalIgnoreCase))
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                address, @"DB(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success) int.TryParse(match.Groups[1].Value, out dbNumber);
        }

        var symbolEntry = _symbolService.GetAllSymbols()
            .FirstOrDefault(s => s.Address == address);

        var binding = new ControlBinding
        {
            Address = address,
            Type = bindingType,
            Name = name,
            Group = group,
            DataType = dataType,
            DbNumber = dbNumber,
            StringMaxLength = stringMaxLength,
            SymbolName = symbolEntry?.Name ?? ""
        };

        _bindingService.SaveBinding(binding);
        LoadBindings();
    }

    /// <summary>
    /// 删除控件
    /// </summary>
    [RelayCommand]
    private async Task DeleteControlAsync(ControlItem? item)
    {
        if (item == null) return;

        var confirm = await UIHelper.DisplayConfirm(
            "确认删除",
            $"确定要删除控件 \"{item.Binding.Name}\" 吗？",
            "删除",
            "取消");

        if (!confirm) return;

        _bindingService.RemoveBinding(item.Binding.Address);
        LoadBindings();
    }

    /// <summary>
    /// 打开趋势监控
    /// </summary>
    [RelayCommand]
    private async Task OpenTrendAsync(ControlItem? item)
    {
        if (item == null) return;

        // 将 ControlBinding 转换为 MonitorVariable
        var variable = new MonitorVariable
        {
            Name = item.Binding.Name,
            Address = item.Binding.Address,
            S7Address = item.Binding.Address,
            DataType = item.Binding.DataType,
            DbNumber = 0,
            ByteOffset = 0,
            BitOffset = 0,
            Size = 0
        };

        // TODO: 趋势弹窗待迁移
        await UIHelper.DisplayAlert("提示", "趋势监控功能正在迁移中", "确定");
    }

    /// <summary>
    /// 编辑控件
    /// </summary>
    [RelayCommand]
    private async Task EditControlAsync(ControlItem? item)
    {
        if (item == null) return;


        // 1. 修改名称
        var name = await UIHelper.DisplayPrompt(
            "编辑控件",
            "控件名称：",
            initialValue: item.Binding.Name);

        if (string.IsNullOrWhiteSpace(name)) return;

        // 2. 修改分组
        var group = await UIHelper.DisplayPrompt(
            "编辑分组",
            "分组名称：",
            initialValue: item.Binding.Group);

        if (string.IsNullOrWhiteSpace(group)) group = "默认";

        // 3. 更新绑定
        item.Binding.Name = name;
        item.Binding.Group = group;
        _bindingService.UpdateBinding(item.Binding);
        LoadBindings();
    }

    /// <summary>
    /// 快速添加轴控制组
    /// </summary>
    [RelayCommand]
    private async Task AddAxisControlGroupAsync()
    {

        // 1. 输入轴名称
        var axisName = await UIHelper.DisplayPrompt(
            "添加轴控制",
            "请输入轴名称：",
            placeholder: "例如: Y轴");

        if (string.IsNullOrWhiteSpace(axisName)) return;

        // 2. 选择轴类型
        var axisTypeChoice = await UIHelper.DisplayActionSheet(
            "选择轴类型", "取消", null,
            "📏 线性轴（有正负限位+原点）",
            "🔄 旋转轴（仅有原点）");
        if (axisTypeChoice == "取消" || string.IsNullOrEmpty(axisTypeChoice)) return;
        var axisType = axisTypeChoice.StartsWith("🔄") ? AxisType.Rotary : AxisType.Linear;

        // 3. 直接打开轴地址配置窗口
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var configWin = new Views.AxisConfigWindow(axisName, axisType, _symbolService);
            configWin.Owner = System.Windows.Application.Current.MainWindow;
            if (configWin.ShowDialog() == true && configWin.Result != null)
            {
                var config = configWin.Result;
                config.Name = axisName;
                config.AxisType = axisType;

                _axisConfigService.Save(config);
                LoadBindings();
            }
        });
    }

    /// <summary>
    /// 打开轴控制页面
    /// </summary>
    [RelayCommand]
    private Task OpenAxisControlAsync(string axisId)
    {
        if (string.IsNullOrEmpty(axisId)) return Task.CompletedTask;

        // 在 UI 线程打开轴控制窗口
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var config = _axisConfigService.GetById(axisId);
            if (config == null) return;
            var controlWin = new Views.AxisControlWindow(config, _plcService);
            controlWin.Owner = System.Windows.Application.Current.MainWindow;
            controlWin.Show();
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// 删除轴配置
    /// </summary>
    [RelayCommand]
    private async Task DeleteAxisConfigAsync(string axisId)
    {
        if (string.IsNullOrEmpty(axisId)) return;

        var config = _axisConfigService.GetById(axisId);
        if (config == null) return;

        var confirm = await UIHelper.DisplayConfirm(
            "确认删除",
            $"确定要删除轴 \"{config.Name}\" 吗？",
            "删除", "取消");

        if (!confirm) return;

        _axisConfigService.Delete(axisId);
        LoadBindings();
        await UIHelper.DisplayAlert("成功", $"已删除轴 \"{config.Name}\"", "确定");
    }

    // ═══════════════ IDisposable ═══════════════

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopRefresh(); // 停止并释放 Timer
        }
        base.Dispose(disposing);
    }
}
