using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace S7WpfApp.Models;

/// <summary>
/// 控件组（用于分组显示）
/// </summary>
public partial class ControlGroup : ObservableObject
{
    public string Name { get; set; } = "";
    public ObservableCollection<ControlItem> Controls { get; } = new();
}

/// <summary>
/// 单个控件项
/// </summary>
public partial class ControlItem : ObservableObject
{
    public ControlBinding Binding { get; set; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayValue))]
    private object? _currentValue;

    [ObservableProperty]
    private bool _isPressed;

    [ObservableProperty]
    private string _inputValue = "";

    /// <summary>
    /// 显示值（格式化后）
    /// </summary>
    public string DisplayValue => CurrentValue?.ToString() ?? "--";
}
