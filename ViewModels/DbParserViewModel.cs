using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using S7WpfApp.Services;
using S7WpfApp.Models;
using Microsoft.Extensions.DependencyInjection;

namespace S7WpfApp.ViewModels;

/// <summary>
/// DB 解析器页面 ViewModel
/// </summary>
public partial class DbParserViewModel : BaseViewModel
{
    private readonly IPlcService _plcService;
    private readonly ISymbolService _symbolService;
    private readonly IPlcDataReadWriteService _readWriteService;
    private readonly TiaDbParser _tiaParser = new();
    private readonly VariableParserService _varParser;
    private System.Timers.Timer? _refreshTimer;
    private List<PlcTag>? _currentTags;
    private string _currentDbFilePath = "";  // 当前导入的 DB 文件路径

    public ObservableCollection<MonitorVariable> Variables { get; } = new();

    [ObservableProperty]
    private string _dbInfo = "请导入 DB 文件";

    [ObservableProperty]
    private int _dbNumber = 1;

    [ObservableProperty]
    private MonitorVariable? _selectedVariable;

    [ObservableProperty]
    private string _writeValue = "";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _autoRefresh;

    [ObservableProperty]
    private int _refreshInterval = 500;

    /// <summary>
    /// 连接延迟 (ms)
    /// </summary>
    [ObservableProperty]
    private long _latency;

    [ObservableProperty]
    private bool _expandArrays = true;  // 默认展开数组

    /// <summary>
    /// 当前已导入的 DB 文件名
    /// </summary>
    [ObservableProperty]
    private string _currentDbFileName = "";

    /// <summary>
    /// 是否已导入 DB 文件
    /// </summary>
    [ObservableProperty]
    private bool _hasImportedDb;

    // ========== 绑定相关属性 ==========
    private readonly IBindingService _bindingService;
    private readonly IDbFileService _dbFileService;

    /// <summary>
    /// 绑定类型列表（用于Picker）
    /// </summary>
    public List<string> BindingTypeOptions { get; } = new()
    {
        "无",
        "点动按钮",
        "保持按钮",
        "数值输入",
        "状态指示"
    };

    public DbParserViewModel(IPlcService plcService, IBindingService bindingService, ISymbolService symbolService, IDbFileService dbFileService, IPlcDataReadWriteService readWriteService)
    {
        _plcService = plcService;
        _bindingService = bindingService;
        _symbolService = symbolService;
        _dbFileService = dbFileService;
        _readWriteService = readWriteService;
        _varParser = new VariableParserService(_tiaParser);
        Title = "DB 解析器";

        _plcService.LatencyUpdated += OnLatencyUpdated;
        _plcService.ConnectionStatusChanged += (s, connected) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                IsConnected = connected;
                if (!connected)
                {
                    StopAutoRefresh();
                }
            });
        };

        _plcService.ErrorOccurred += (s, error) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => SetError(error));
        };

        // 订阅趋势采样独占模式（暂停/恢复自动刷新）
        _plcService.RefreshPauseRequested += (s, paused) =>
        {
            if (paused)
                StopAutoRefresh();
            else if (AutoRefresh && IsConnected)
                StartAutoRefresh();
        };

        // 初始检查连接状态
        IsConnected = _plcService.IsConnected;

        // 加载已保存的绑定配置
        LoadBindingsFromService();
    }

    /// <summary>
    /// 从服务加载已保存的绑定到变量
    /// </summary>
    private void LoadBindingsFromService()
    {
        var bindings = _bindingService.GetBindings();
        foreach (var variable in Variables)
        {
            var binding = bindings.FirstOrDefault(b => b.Address == variable.S7Address);
            if (binding != null)
            {
                variable.BindingType = binding.Type;
                variable.BindingName = binding.Name;
                variable.BindingGroup = binding.Group;
            }
        }
    }

    /// <summary>
    /// 保存变量绑定
    /// </summary>
    [RelayCommand]
    private async Task SaveBindingAsync(MonitorVariable? variable)
    {
        if (variable == null) return;

        if (variable.BindingType == BindingType.None)
        {
            // 移除绑定
            _bindingService.RemoveBinding(variable.S7Address);
        }
        else
        {
            // 查找符号名称（通过地址反向查找）
            var symbolEntry = _symbolService.GetAllSymbols()
                .FirstOrDefault(s => s.Address == variable.S7Address);

            // 保存绑定
            var binding = new ControlBinding
            {
                Address = variable.S7Address,
                Type = variable.BindingType,
                Name = string.IsNullOrWhiteSpace(variable.BindingName) ? variable.CleanName : variable.BindingName,
                Group = variable.BindingGroup,
                DataType = variable.DataType,
                DbNumber = variable.DbNumber,
                SymbolName = symbolEntry?.Name ?? ""  // 关联符号名用于动态更新
            };
            _bindingService.SaveBinding(binding);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 配置变量绑定（弹出对话框）
    /// </summary>
    [RelayCommand]
    private async Task ConfigureBindingAsync(MonitorVariable? variable)
    {
        if (variable == null) return;

        // 使用 DisplayActionSheet 选择绑定类型
        var action = await UIHelper.DisplayActionSheet(
            $"为 {variable.CleanName} 选择绑定类型",
            "取消",
            "移除绑定",
            "点动按钮", "保持按钮", "数值输入", "状态指示");

        if (action == "取消") return;

        if (action == "移除绑定")
        {
            variable.BindingType = BindingType.None;
            variable.BindingName = "";
            _bindingService.RemoveBinding(variable.S7Address);
            return;
        }

        // 设置绑定类型
        variable.BindingType = action switch
        {
            "点动按钮" => BindingType.MomentaryButton,
            "保持按钮" => BindingType.ToggleButton,
            "数值输入" => BindingType.NumericInput,
            "状态指示" => BindingType.StatusIndicator,
            _ => BindingType.None
        };

        // 输入控件名称
        var name = await UIHelper.DisplayPrompt(
            "控件名称",
            "请输入控件显示名称：",
            initialValue: string.IsNullOrWhiteSpace(variable.BindingName) ? variable.CleanName : variable.BindingName);

        if (string.IsNullOrWhiteSpace(name)) name = variable.CleanName;
        variable.BindingName = name;

        // 输入分组名称
        var group = await UIHelper.DisplayPrompt(
            "分组",
            "请输入分组名称：",
            initialValue: variable.BindingGroup);

        if (string.IsNullOrWhiteSpace(group)) group = "默认";
        variable.BindingGroup = group;

        // 保存绑定
        await SaveBindingAsync(variable);
    }

    private void OnLatencyUpdated(object? sender, long latency)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Latency = latency;
        });
    }

    /// <summary>
    /// 导入 DB 文件
    /// </summary>
    [RelayCommand]
    private async Task ImportDbFileAsync()
    {
        try
        {
            var pickedPath = UIHelper.PickFile("选择文件", "DB文件|*.db;*.txt;*.awl|所有文件|*.*"); var result = pickedPath != null ? new { FullPath = pickedPath, FileName = System.IO.Path.GetFileName(pickedPath) } : null;

            if (result == null) return;

            await ImportDbFileFromPathAsync(result.FullPath, null);
        }
        catch (Exception ex)
        {
            SetError($"导入失败: {ex.Message}");
            IsBusy = false;
        }
    }

    /// <summary>
    /// 快速重新导入当前 DB 文件
    /// </summary>
    [RelayCommand]
    private async Task QuickReimportDbFileAsync()
    {
        if (string.IsNullOrEmpty(_currentDbFilePath) || !File.Exists(_currentDbFilePath))
        {
            await UIHelper.DisplayAlert("提示", "请先导入一个 DB 文件", "确定");
            return;
        }

        await ImportDbFileFromPathAsync(_currentDbFilePath, DbNumber);
    }

    /// <summary>
    /// 从路径导入 DB 文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="knownDbNumber">已知的 DB 编号（如果为 null 则弹出确认窗口）</param>
    public async Task ImportDbFileFromPathAsync(string filePath, int? knownDbNumber)
    {
        try
        {
            IsBusy = true;
            var fileName = Path.GetFileName(filePath);
            SetStatus($"正在解析: {fileName}");

            // 保存文件路径用于符号绑定
            _currentDbFilePath = filePath;

            // 读取文件内容
            var content = await File.ReadAllTextAsync(filePath);

            // 尝试从文件名或内容中提取 DB 编号
            int detectedDbNumber = VariableParserService.DetectDbNumber(filePath, content);
            int dbNumber;

            if (knownDbNumber.HasValue)
            {
                // 快速重新导入使用已知编号
                dbNumber = knownDbNumber.Value;
            }
            else
            {
                // 首次导入弹出确认窗口
                var inputDbNumber = await UIHelper.DisplayPrompt(
                    "确认 DB 编号",
                    $"检测到可能的 DB 编号: {detectedDbNumber}\n请确认或修改：",
                    placeholder: "DB 编号",
                    initialValue: detectedDbNumber.ToString());

                if (string.IsNullOrWhiteSpace(inputDbNumber))
                {
                    IsBusy = false;
                    return;
                }

                if (!int.TryParse(inputDbNumber, out dbNumber))
                {
                    dbNumber = detectedDbNumber;
                }
            }

            DbNumber = dbNumber;

            // 解析 DB 文件
            _currentTags = _tiaParser.Parse(content, DbNumber);

            if (_currentTags != null && _currentTags.Count > 0)
            {
                RefreshVariableList();
                DbInfo = $"DB{DbNumber} - {_tiaParser.ParsedDbName} ({_currentTags.Count} 个变量)";

                // 显示当前文件名
                CurrentDbFileName = fileName;
                HasImportedDb = true;

                // 更新已绑定符号的地址
                int updatedCount = _symbolService.UpdateAddressesFromParsedTags(DbNumber, _currentTags);

                // 复制到程序目录的 PlcDB 文件夹
                var plcDbFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PlcDB");
                Directory.CreateDirectory(plcDbFolder);
                var localPath = Path.Combine(plcDbFolder, fileName);
                var fullSrc = Path.GetFullPath(filePath);
                var fullDst = Path.GetFullPath(localPath);
                if (!string.Equals(fullSrc, fullDst, StringComparison.OrdinalIgnoreCase))
                    File.Copy(filePath, localPath, overwrite: true);

                // 保存到 DB 文件管理服务（使用本地路径）
                var dbEntry = new DbFileEntry
                {
                    FilePath = localPath,
                    FileName = fileName,
                    DbNumber = DbNumber,
                    DbName = _tiaParser.ParsedDbName,
                    VariableCount = _currentTags.Count
                };
                _dbFileService.AddOrUpdate(dbEntry);
                await _dbFileService.SaveAsync();

                if (updatedCount > 0)
                {
                    SetStatus($"解析成功，共 {_currentTags.Count} 个变量，更新了 {updatedCount} 个符号地址");
                }
                else
                {
                    SetStatus($"解析成功，共 {_currentTags.Count} 个变量");
                }
            }
            else
            {
                SetError("无法解析文件或文件为空");
            }
        }
        catch (Exception ex)
        {
            SetError($"导入失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }



    /// <summary>
    /// 刷新变量列表 - 将 PlcTag 转换为 MonitorVariable
    /// </summary>
    private void RefreshVariableList()
    {
        Variables.Clear();
        if (_currentTags == null) return;

        // 预先获取所有符号用于快速查找
        var symbolsByAddress = _symbolService.GetAllSymbols()
            .ToDictionary(s => s.Address, s => s.Name);

        foreach (var tag in _currentTags)
        {
            // 计算缩进显示名称
            string indent = new string(' ', tag.Level * 2);
            string displayName = tag.IsContainer
                ? $"{indent}{tag.ExpandIcon} {tag.DisplayName}"
                : $"{indent}  {tag.DisplayName}";

            // 查找绑定的符号名
            symbolsByAddress.TryGetValue(tag.Address, out var boundSymbolName);

            Variables.Add(new MonitorVariable
            {
                Name = tag.SymbolicName,
                DisplayName = tag.DisplayName,
                DataType = tag.DisplayDataType,
                Address = tag.Address,
                S7Address = tag.Address,  // TiaDbParser 已生成正确格式
                DbNumber = DbNumber,
                ByteOffset = VariableParserService.ParseByteOffset(tag.Address),
                BitOffset = VariableParserService.ParseBitOffset(tag.Address),
                Size = _tiaParser.GetReadByteCount(tag),
                Comment = tag.Comment,
                IsArrayElement = tag.ParentSymbolicName?.Contains("[") ?? false,
                IsArrayParent = tag.IsContainer,
                ParentArrayName = tag.ParentSymbolicName ?? "",
                IsSelected = !tag.IsContainer,  // 容器类型默认不选中
                IsExpanded = tag.Expander != "[+]",
                IsVisible = tag.IsVisible,
                Depth = tag.Level,
                BoundSymbolName = boundSymbolName ?? ""  // 设置绑定的符号名
            });
        }

        // 手动通知视图刷新变量列表
        OnPropertyChanged(nameof(Variables));
    }



    /// <summary>
    /// 切换变量展开/折叠状态（动态展开）
    /// </summary>
    [RelayCommand]
    private void ToggleArrayExpand(MonitorVariable? variable)
    {
        if (variable == null || !variable.IsExpandable)
        {
            SetStatus($"点击了非可展开变量: {variable?.Name ?? "null"}, IsExpandable={variable?.IsExpandable}");
            return;
        }

        variable.IsExpanded = !variable.IsExpanded;
        SetStatus($"切换 {variable.Name}: IsExpanded={variable.IsExpanded}");

        // 更新显示图标
        if (variable.IsExpanded)
        {
            variable.DisplayName = variable.DisplayName.Replace("▶", "▼");

            // 动态展开：插入子元素
            ExpandVariableChildren(variable);
        }
        else
        {
            variable.DisplayName = variable.DisplayName.Replace("▼", "▶");

            // 折叠：移除子元素
            CollapseVariableChildren(variable);
            SetStatus($"折叠完成，剩余 {Variables.Count} 个变量");
        }
    }

    /// <summary>
    /// 展开变量的子元素 - 切换可见性
    /// </summary>
    private void ExpandVariableChildren(MonitorVariable parent)
    {
        var parentName = parent.Name;

        // 查找所有直接子元素（ParentArrayName 或 Name 以 parent.Name 开头）
        foreach (var v in Variables)
        {
            if (v.ParentArrayName == parentName ||
                v.Name.StartsWith(parentName + ".") ||
                v.Name.StartsWith(parentName + "["))
            {
                // 只显示直接子元素（深度差为 1）
                if (v.Depth == parent.Depth + 1)
                {
                    v.IsVisible = true;
                }
            }
        }

        SetStatus($"展开 {parentName}，显示直接子元素");
    }

    /// <summary>
    /// 折叠变量的子元素 - 隐藏子元素
    /// </summary>
    private void CollapseVariableChildren(MonitorVariable parent)
    {
        var parentName = parent.Name;

        // 查找所有子元素并隐藏（同时折叠嵌套展开项）
        foreach (var v in Variables)
        {
            if (v.ParentArrayName == parentName ||
                v.Name.StartsWith(parentName + ".") ||
                v.Name.StartsWith(parentName + "["))
            {
                v.IsVisible = false;
                // 同时折叠所有嵌套的展开项
                if (v.IsExpanded)
                {
                    v.IsExpanded = false;
                    v.DisplayName = v.DisplayName.Replace("▼", "▶");
                }
            }
        }
    }



    /// <summary>
    /// 更新 DB 编号
    /// </summary>
    partial void OnDbNumberChanged(int value)
    {
        if (_currentTags != null && _currentTags.Count > 0)
        {
            // 需要重新解析才能更新 DB 编号，暂时只更新显示
            RefreshVariableList();
            DbInfo = $"DB{value} - {_tiaParser.ParsedDbName} ({_currentTags.Count} 个变量)";
        }
    }

    /// <summary>
    /// 读取所有选中变量（委托 PlcDataReadWriteService 执行批量读取）
    /// </summary>
    [RelayCommand]
    private async Task ReadAllAsync()
    {
        if (!IsConnected || IsBusy) return;

        try
        {
            IsBusy = true;

            var selectedVars = Variables.Where(x => x.IsSelected && !x.IsArrayParent).ToList();
            if (selectedVars.Count == 0)
            {
                SetStatus("没有选中的变量");
                return;
            }

            var (totalRequests, elapsedMs) = await _readWriteService.ReadVariablesAsync(selectedVars);

            // 清空父项的值显示
            foreach (var parent in Variables.Where(x => x.IsArrayParent))
                parent.CurrentValue = "";

            SetStatus($"读取完成 - {selectedVars.Count} 变量，{totalRequests} 请求，{elapsedMs}ms");
        }
        finally
        {
            IsBusy = false;
        }
    }



    /// <summary>
    /// 写入选中变量（委托 PlcDataReadWriteService 执行写入）
    /// </summary>
    [RelayCommand]
    private async Task WriteValueAsync()
    {
        if (!IsConnected || SelectedVariable == null || string.IsNullOrWhiteSpace(WriteValue))
        {
            SetError("请选择变量并输入值");
            return;
        }

        try
        {
            IsBusy = true;
            var v = SelectedVariable;

            await _readWriteService.WriteVariableAsync(v, WriteValue);

            WriteValue = "";
            SetStatus($"写入成功: {v.Name}");
            await ReadAllAsync();
        }
        catch (Exception ex)
        {
            SetError($"写入失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 切换自动刷新
    /// </summary>
    [RelayCommand]
    private void ToggleAutoRefresh()
    {
        AutoRefresh = !AutoRefresh;
        if (AutoRefresh)
        {
            StartAutoRefresh();
        }
        else
        {
            StopAutoRefresh();
        }
    }

    private void StartAutoRefresh()
    {
        if (_refreshTimer != null) return;

        _refreshTimer = new System.Timers.Timer(RefreshInterval);
        _refreshTimer.Elapsed += async (s, e) =>
        {
            if (IsConnected && !IsBusy)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(ReadAllAsync);
            }
        };
        _refreshTimer.Start();
        SetStatus($"自动刷新已启动 ({RefreshInterval}ms)");
    }

    private void StopAutoRefresh()
    {
        if (_refreshTimer != null)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _refreshTimer = null;
            AutoRefresh = false;
            SetStatus("自动刷新已停止");
        }
    }

    /// <summary>
    /// 全选变量
    /// </summary>
    [RelayCommand]
    private void SelectAllVariables()
    {
        foreach (var v in Variables)
        {
            v.IsSelected = true;
        }
        OnPropertyChanged(nameof(Variables));
    }

    /// <summary>
    /// 取消全选
    /// </summary>
    [RelayCommand]
    private void DeselectAllVariables()
    {
        foreach (var v in Variables)
        {
            v.IsSelected = false;
        }
        OnPropertyChanged(nameof(Variables));
    }

    /// <summary>
    /// 打开趋势监控弹窗 (切换为多轨分屏视图)
    /// </summary>
    [RelayCommand]
    private void OpenTrendPopup(MonitorVariable? variable)
    {
        if (variable == null)
        {
            SetStatus("请先选择一个变量");
            return;
        }

        if (variable.IsArrayParent)
        {
            SetStatus("请选择基本类型变量进行趋势监控");
            return;
        }

        try
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // 查找是否已经打开了多变量趋势窗口
                var multiTrendWindow = System.Windows.Application.Current.Windows
                    .OfType<Views.MultiTrendWindow>()
                    .FirstOrDefault();

                if (multiTrendWindow == null)
                {
                    // 创建新窗口
                    multiTrendWindow = new Views.MultiTrendWindow(
                        App.Services.GetRequiredService<ViewModels.MultiTrendViewModel>())
                    {
                        Owner = System.Windows.Application.Current.MainWindow
                    };
                    multiTrendWindow.Show();
                }
                else
                {
                    // 如果已打开，激活并置前
                    if (multiTrendWindow.WindowState == WindowState.Minimized)
                        multiTrendWindow.WindowState = WindowState.Normal;
                    multiTrendWindow.Activate();
                }

                // 将该变量作为一个新通道追加进去
                multiTrendWindow.AddChannel(variable.Name, variable.Address);
            });
        }
        catch (Exception ex)
        {
            SetStatus($"打开趋势视图失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 添加变量到符号表（允许自定义名称）
    /// </summary>
    [RelayCommand]
    private async Task AddToSymbolTableAsync(MonitorVariable variable)
    {
        if (variable == null) return;

        // 检查地址是否已存在符号
        if (_symbolService.AddressHasSymbol(variable.Address))
        {
            var existingName = _symbolService.GetSymbolName(variable.Address);
            await UIHelper.DisplayAlert("提示", $"该地址已存在符号：{existingName}", "确定");
            return;
        }

        // 弹出输入框让用户自定义符号名称
        var symbolName = await UIHelper.DisplayPrompt(
            "添加到符号表",
            "请输入符号名称（用于引用此变量）：",
            placeholder: "例如: 电机1速度",
            initialValue: variable.Name);

        if (string.IsNullOrWhiteSpace(symbolName)) return;

        // 检查名称是否已存在
        if (_symbolService.SymbolExists(symbolName))
        {
            await UIHelper.DisplayAlert("错误", $"符号名称 \"{symbolName}\" 已被使用", "确定");
            return;
        }

        // 选择分组
        string category = "";
        var existingCategories = _symbolService.GetCategories().ToList();

        // 构建分组选择列表
        var groupOptions = new List<string> { "📁 新建分组" };
        groupOptions.AddRange(existingCategories.Select(c => $"📂 {c}"));

        var groupChoice = await UIHelper.DisplayActionSheet(
            "选择符号分组", "取消", null, groupOptions.ToArray());

        if (groupChoice == "取消" || string.IsNullOrEmpty(groupChoice)) return;

        if (groupChoice == "📁 新建分组")
        {
            var newGroup = await UIHelper.DisplayPrompt(
                "新建分组", "请输入分组名称：",
                placeholder: "例如: 电机控制",
                initialValue: $"DB{DbNumber}");
            if (string.IsNullOrWhiteSpace(newGroup)) return;
            category = newGroup;
        }
        else
        {
            category = groupChoice.Replace("📂 ", "");
        }

        // 添加到符号表
        _symbolService.AddSymbol(
            name: symbolName,
            address: variable.Address,
            dataType: variable.DataType,
            originalSymbolPath: variable.Name,
            dbNumber: DbNumber,
            dbFilePath: _currentDbFilePath,
            comment: variable.Comment,
            category: category);

        variable.BoundSymbolName = symbolName;
        await UIHelper.DisplayAlert("成功", $"已添加符号 \"{symbolName}\" → {variable.Address}\n分组: {category}", "确定");
        SetStatus($"已添加符号: {symbolName} → {category}");
    }

    // ═══════════════ IDisposable ═══════════════

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopAutoRefresh(); // 停止并释放 Timer
            _plcService.LatencyUpdated -= OnLatencyUpdated;
        }
        base.Dispose(disposing);
    }
}

