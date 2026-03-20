using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using S7WpfApp.Models;
using S7WpfApp.Services;

namespace S7WpfApp.ViewModels;

/// <summary>
/// 配方管理 ViewModel
/// </summary>
public partial class RecipeViewModel : BaseViewModel
{
    private readonly IRecipeService _recipeService;
    private readonly IPlcService _plcService;
    private readonly IAuthService _authService;
    private readonly ISymbolService _symbolService;
    private readonly IRecipeLogService _logService;

    /// <summary>
    /// 配方表列表
    /// </summary>
    public ObservableCollection<RecipeTable> Tables { get; } = new();

    /// <summary>
    /// 当前选中的配方表
    /// </summary>
    [ObservableProperty]
    private RecipeTable? _selectedTable;

    /// <summary>
    /// 当前选中的数据集
    /// </summary>
    [ObservableProperty]
    private RecipeDataSet? _selectedDataSet;

    /// <summary>
    /// 当前配方表的数据集列表
    /// </summary>
    public ObservableCollection<RecipeDataSet> DataSets { get; } = new();

    /// <summary>
    /// 当前数据集的变量显示列表
    /// </summary>
    public ObservableCollection<RecipeVariableRow> VariableRows { get; } = new();

    /// <summary>
    /// 日志文本
    /// </summary>
    [ObservableProperty]
    private string _logText = "";

    /// <summary>
    /// 是否为管理员
    /// </summary>
    public bool IsAdmin => _authService.IsAdmin;

    /// <summary>
    /// PLC 是否已连接
    /// </summary>
    public bool IsConnected => _plcService.IsConnected;

    public RecipeViewModel(IRecipeService recipeService, IPlcService plcService,
        IAuthService authService, ISymbolService symbolService, IRecipeLogService logService)
    {
        _recipeService = recipeService;
        _plcService = plcService;
        _authService = authService;
        _symbolService = symbolService;
        _logService = logService;

        Title = "配方管理";

        // 订阅日志事件
        _logService.LogAdded += (_, msg) =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                LogText = msg + Environment.NewLine + LogText;
                // 限制日志显示行数
                if (LogText.Length > 10000)
                    LogText = LogText[..8000];
            });
        };

        // 订阅连接状态变化
        _plcService.ConnectionStatusChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsConnected));
        };

        // 加载数据
        RefreshTables();
        LogText = _logService.GetTodayLog();
    }

    /// <summary>
    /// 刷新配方表列表
    /// </summary>
    private void RefreshTables()
    {
        Tables.Clear();
        foreach (var t in _recipeService.GetTables())
            Tables.Add(t);
    }

    /// <summary>
    /// 选中配方表变化时
    /// </summary>
    partial void OnSelectedTableChanged(RecipeTable? value)
    {
        DataSets.Clear();
        VariableRows.Clear();
        SelectedDataSet = null;

        if (value != null)
        {
            foreach (var ds in value.DataSets)
                DataSets.Add(ds);
            if (DataSets.Count > 0)
                SelectedDataSet = DataSets[0];
        }
    }

    /// <summary>
    /// 选中数据集变化时
    /// </summary>
    partial void OnSelectedDataSetChanged(RecipeDataSet? value)
    {
        RefreshVariableRows();
    }

    /// <summary>
    /// 刷新变量行显示
    /// </summary>
    private void RefreshVariableRows()
    {
        VariableRows.Clear();
        if (SelectedTable == null) return;

        foreach (var varDef in SelectedTable.Variables)
        {
            var row = new RecipeVariableRow
            {
                Name = varDef.Name,
                Address = varDef.Address,
                SymbolName = varDef.SymbolName,
                DataType = varDef.DataType,
                Comment = varDef.Comment,
                Value = SelectedDataSet?.Values.TryGetValue(varDef.Name, out var val) == true ? val : "",
                PlcValue = "" // 初始化为空
            };
            VariableRows.Add(row);
        }
    }

    /// <summary>
    /// 将 VariableRows 中的值同步回数据集
    /// </summary>
    private void SyncValuesToDataSet()
    {
        if (SelectedDataSet == null || SelectedTable == null) return;

        // 记录变更的变量（旧值 → 新值）
        var changes = new System.Collections.Generic.List<string>();

        foreach (var row in VariableRows)
        {
            string oldValue = SelectedDataSet.Values.TryGetValue(row.Name, out var ov) ? ov : "";
            string newValue = row.Value ?? "";

            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                changes.Add($"{row.Name}: {oldValue} → {newValue}");
            }

            SelectedDataSet.Values[row.Name] = newValue;
        }

        SelectedDataSet.ModifiedAt = DateTime.Now;
        _recipeService.SaveAll();

        // 将变更记录写入日志
        if (changes.Count > 0)
        {
            string detail = string.Join("; ", changes);
            _logService.Log("修改保存", SelectedTable.Name, SelectedDataSet.Name,
                $"变更 {changes.Count} 项: {detail}");
        }
    }

    #region 配方表命令

    /// <summary>
    /// 新建配方表
    /// </summary>
    [RelayCommand]
    private async Task CreateTableAsync()
    {
        var name = await UIHelper.DisplayPrompt("新建配方表", "请输入配方表名称:", placeholder: "如: 配料配方");
        if (string.IsNullOrWhiteSpace(name)) return;

        var table = new RecipeTable { Name = name };
        _recipeService.SaveTable(table);
        RefreshTables();
        SelectedTable = Tables.LastOrDefault();
    }

    /// <summary>
    /// 删除配方表
    /// </summary>
    [RelayCommand]
    private async Task DeleteTableAsync()
    {
        if (SelectedTable == null) return;

        var confirm = await UIHelper.DisplayConfirm("确认删除",
            $"确定要删除配方表 \"{SelectedTable.Name}\" 吗？\n这将删除其下所有数据集。", "删除", "取消");
        if (!confirm) return;

        _recipeService.DeleteTable(SelectedTable.Id);
        RefreshTables();
        SelectedTable = Tables.FirstOrDefault();
    }

    #endregion

    #region 变量管理命令

    /// <summary>
    /// 添加变量
    /// </summary>
    [RelayCommand]
    private async Task AddVariableAsync()
    {
        if (SelectedTable == null) return;

        // 选择绑定方式
        var bindMode = await UIHelper.DisplayActionSheet("添加变量 - 选择绑定方式", "取消", null,
            "📖 从符号表选择", "✏️ 手动输入地址");
        if (bindMode == null || bindMode == "取消") return;

        string name = "";
        string address = "";
        string symbolName = "";
        string dataType = "Int";

        if (bindMode.Contains("符号表"))
        {
            // 1. 先选择分组 (Category)
            var categories = _symbolService.GetCategories().ToList();
            if (categories.Count == 0)
            {
                await UIHelper.DisplayAlert("提示", "符号表为空或没有分组，请先在符号管理中添加符号。", "确定");
                return;
            }

            var categoryItems = categories.Select(c => string.IsNullOrEmpty(c) ? "未分组" : c).ToArray();
            var selectedCategoryName = await UIHelper.DisplayActionSheet("选择符号分组", "取消", null, categoryItems);
            if (selectedCategoryName == null || selectedCategoryName == "取消") return;

            var category = selectedCategoryName == "未分组" ? "" : selectedCategoryName;

            // 2. 选择该分组下的具体符号
            var symbols = _symbolService.GetSymbolsByCategory(category).ToList();
            if (symbols.Count == 0)
            {
                await UIHelper.DisplayAlert("提示", "该分组下没有符号。", "确定");
                return;
            }

            // 构建选项列表：符号名 | 地址 | 类型
            var options = symbols.Select(s => $"{s.Name}  ({s.Address}, {s.DataType})").ToArray();
            var selected = await UIHelper.DisplayActionSheet("选择符号", "取消", null, options);
            if (selected == null || selected == "取消") return;

            // 找到选中的符号
            var idx = Array.IndexOf(options, selected);
            if (idx < 0) return;
            var sym = symbols[idx];

            name = sym.Name;
            address = sym.Address;
            symbolName = sym.Name;
            dataType = string.IsNullOrEmpty(sym.DataType) ? "Int" : sym.DataType;

            // 允许用户自定义变量显示名称
            var customName = await UIHelper.DisplayPrompt("变量名称",
                $"符号: {sym.Name}\n地址: {sym.Address}\n类型: {sym.DataType}\n\n请确认变量名称（可修改）:",
                initialValue: name);
            if (string.IsNullOrWhiteSpace(customName)) return;
            name = customName;
        }
        else
        {
            // 手动输入
            var inputName = await UIHelper.DisplayPrompt("添加变量", "变量名称:", placeholder: "如: 温度设定值");
            if (string.IsNullOrWhiteSpace(inputName)) return;
            name = inputName;

            var inputAddr = await UIHelper.DisplayPrompt("添加变量", "PLC 地址:",
                placeholder: "如: DB200.DBW0");
            if (string.IsNullOrWhiteSpace(inputAddr)) return;
            address = inputAddr;

            var typeAction = await UIHelper.DisplayActionSheet("数据类型", "取消", null,
                "Bool", "Byte", "Char", "Int", "DInt", "Real");
            if (typeAction == null || typeAction == "取消") return;
            dataType = typeAction;
        }

        var varDef = new RecipeVariableDefinition
        {
            Name = name,
            Address = address,
            SymbolName = symbolName,
            DataType = dataType,
        };

        SelectedTable.Variables.Add(varDef);

        // 给所有现有数据集添加默认值
        foreach (var ds in SelectedTable.DataSets)
        {
            if (!ds.Values.ContainsKey(name))
                ds.Values[name] = GetDefaultValue(dataType);
        }

        _recipeService.SaveTable(SelectedTable);
        RefreshVariableRows();
    }

    /// <summary>
    /// 移除变量
    /// </summary>
    [RelayCommand]
    private void RemoveVariable(RecipeVariableRow? row)
    {
        if (SelectedTable == null || row == null) return;

        var varDef = SelectedTable.Variables.FirstOrDefault(v => v.Name == row.Name);
        if (varDef != null)
        {
            SelectedTable.Variables.Remove(varDef);
            // 从所有数据集中移除对应值
            foreach (var ds in SelectedTable.DataSets)
                ds.Values.Remove(row.Name);

            _recipeService.SaveTable(SelectedTable);
            RefreshVariableRows();
        }
    }

    private static string GetDefaultValue(string dataType) => dataType.ToLower() switch
    {
        "bool" => "False",
        "real" => "0.0",
        _ => "0"
    };

    #endregion

    #region 数据集命令

    /// <summary>
    /// 新建数据集
    /// </summary>
    [RelayCommand]
    private async Task CreateDataSetAsync()
    {
        if (SelectedTable == null) return;

        var name = await UIHelper.DisplayPrompt("新建数据集", "请输入数据集名称:", placeholder: "如: 产品A");
        if (string.IsNullOrWhiteSpace(name)) return;

        var ds = _recipeService.AddDataSet(SelectedTable, name);
        DataSets.Add(ds);
        SelectedDataSet = ds;
    }

    /// <summary>
    /// 删除数据集
    /// </summary>
    [RelayCommand]
    private async Task DeleteDataSetAsync()
    {
        if (SelectedTable == null || SelectedDataSet == null) return;

        var confirm = await UIHelper.DisplayConfirm("确认删除",
            $"确定要删除数据集 \"{SelectedDataSet.Name}\" 吗？", "删除", "取消");
        if (!confirm) return;

        _recipeService.DeleteDataSet(SelectedTable, SelectedDataSet.Id);
        DataSets.Remove(SelectedDataSet);
        SelectedDataSet = DataSets.FirstOrDefault();
    }

    #endregion

    #region PLC 读写命令

    /// <summary>
    /// 从 PLC 读取
    /// </summary>
    [RelayCommand]
    private async Task ReadFromPlcAsync()
    {
        if (SelectedTable == null || SelectedDataSet == null || !IsConnected) return;

        try
        {
            IsBusy = true;
            // 复制一个临时的 DataSet 供读取服务写入
            var tempDataSet = new RecipeDataSet { Id = SelectedDataSet.Id, Name = "Temp" };

            // 调用读取服务，读到的值会写在 tempDataSet.Values 中
            await _recipeService.ReadFromPlcAsync(SelectedTable, tempDataSet);

            // 将读上来的值赋给界面对应的 PlcValue，而不修改原有配方设定值 Value
            foreach (var row in VariableRows)
            {
                if (tempDataSet.Values.TryGetValue(row.Name, out var plcVal))
                {
                    row.PlcValue = plcVal;
                }
                else
                {
                    row.PlcValue = "读取失败";
                }
            }

            SetStatus("从 PLC 读取配方完成");
        }
        catch (Exception ex)
        {
            SetError($"读取失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 写入 PLC
    /// </summary>
    [RelayCommand]
    private async Task WriteToPlcAsync()
    {
        if (SelectedTable == null || SelectedDataSet == null || !IsConnected) return;

        var confirm = await UIHelper.DisplayConfirm("确认写入",
            $"确定要将数据集 \"{SelectedDataSet.Name}\" 的值写入 PLC 吗？\n此操作将覆盖 PLC 中的当前值。",
            "写入", "取消");
        if (!confirm) return;

        try
        {
            IsBusy = true;
            // 先同步界面值到数据集
            SyncValuesToDataSet();
            await _recipeService.WriteToPlcAsync(SelectedTable, SelectedDataSet);
            SetStatus("配方值已写入 PLC");
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

    #endregion

    #region CSV 导入/导出

    /// <summary>
    /// 导出 CSV
    /// </summary>
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (SelectedTable == null) return;

        var filePath = UIHelper.SaveFile("导出配方 CSV", "CSV 文件|*.csv",
            $"{SelectedTable.Name}.csv");
        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            IsBusy = true;
            // 先同步界面值
            SyncValuesToDataSet();
            await _recipeService.ExportToCsvAsync(SelectedTable, filePath);
            SetStatus($"配方已导出到 {filePath}");
        }
        catch (Exception ex)
        {
            SetError($"导出失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 导入 CSV
    /// </summary>
    [RelayCommand]
    private async Task ImportCsvAsync()
    {
        var filePath = UIHelper.PickFile("导入配方 CSV", "CSV 文件|*.csv");
        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            IsBusy = true;
            var table = await _recipeService.ImportFromCsvAsync(filePath);
            RefreshTables();
            SelectedTable = Tables.FirstOrDefault(t => t.Id == table.Id);
            SetStatus($"已导入配方表: {table.Name}");
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

    #endregion

    /// <summary>
    /// 保存当前编辑的值
    /// </summary>
    [RelayCommand]
    private void SaveValues()
    {
        SyncValuesToDataSet();
        SetStatus("配方值已保存");
    }
}
