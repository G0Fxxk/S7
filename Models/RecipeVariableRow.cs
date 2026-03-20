using CommunityToolkit.Mvvm.ComponentModel;

namespace S7WpfApp.Models;

/// <summary>
/// 配方变量行（用于 DataGrid 显示和编辑）
/// </summary>
public partial class RecipeVariableRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _symbolName = "";
    [ObservableProperty] private string _dataType = "";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private string _plcValue = ""; // 从 PLC 读取的值
    [ObservableProperty] private string _comment = "";
}
