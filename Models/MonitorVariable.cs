using CommunityToolkit.Mvvm.ComponentModel;

namespace S7WpfApp.Models;

/// <summary>
/// 可监控的变量项
/// </summary>
public partial class MonitorVariable : ObservableObject
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public string Address { get; set; } = "";
    public string S7Address { get; set; } = "";  // 用于 S7.net 读写
    public int DbNumber { get; set; }
    public int ByteOffset { get; set; }
    public int BitOffset { get; set; }
    public int Size { get; set; }  // 数据大小（字节）
    public string Comment { get; set; } = "";
    public int Depth { get; set; }  // 嵌套深度（用于缩进显示）

    /// <summary>
    /// 是否是数组元素
    /// </summary>
    public bool IsArrayElement { get; set; }

    /// <summary>
    /// 是否是数组父项（可展开/折叠）
    /// </summary>
    public bool IsArrayParent { get; set; }

    /// <summary>
    /// 父数组名称（用于关联元素和父项）
    /// </summary>
    public string ParentArrayName { get; set; } = "";

    /// <summary>
    /// 是否可展开（复杂类型：Array, Struct, UDT）
    /// </summary>
    public bool IsExpandable => DataType.Contains("Array", StringComparison.OrdinalIgnoreCase) ||
                                DataType.Equals("Struct", StringComparison.OrdinalIgnoreCase) ||
                                DataType.Equals("STRUCT", StringComparison.OrdinalIgnoreCase) ||
                                DataType.Equals("UDT", StringComparison.OrdinalIgnoreCase) ||
                                IsArrayParent ||  // 使用已设置的标志
                                (DataType.StartsWith("\"") && DataType.EndsWith("\""));

    /// <summary>
    /// 是否是字符数组类型
    /// </summary>
    public bool IsCharArray => DataType.Contains("Char", StringComparison.OrdinalIgnoreCase) &&
                               DataType.Contains("Array", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 是否是字符串类型
    /// </summary>
    public bool IsString => DataType.StartsWith("String", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 是否是字节数组类型
    /// </summary>
    public bool IsByteArray => DataType.Contains("Byte", StringComparison.OrdinalIgnoreCase) &&
                               DataType.Contains("Array", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _currentValue = "--";

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandIcon))]
    private bool _isExpanded = false;  // 默认不展开

    [ObservableProperty]
    private bool _isVisible = true;  // 是否可见

    [ObservableProperty]
    private string _displayName = "";  // 带缩进的显示名称

    // ========== 控件绑定属性 ==========

    [ObservableProperty]
    private BindingType _bindingType = BindingType.None;

    [ObservableProperty]
    private string _bindingName = "";

    [ObservableProperty]
    private string _bindingGroup = "默认";

    /// <summary>
    /// 绑定的符号名称（只读，用于显示）
    /// </summary>
    [ObservableProperty]
    private string _boundSymbolName = "";

    /// <summary>
    /// 展开/折叠图标
    /// </summary>
    public string ExpandIcon => IsExpandable ? (IsExpanded ? "▼" : "▶") : "";

    /// <summary>
    /// 干净的变量名（不含图标前缀）
    /// </summary>
    public string CleanName => Name.TrimStart('▶', '▼', ' ');

    /// <summary>
    /// 树形显示名称（带缩进）
    /// </summary>
    public string TreeDisplayName => new string(' ', Depth * 4) + CleanName.Split('.').LastOrDefault() ?? CleanName;
}
