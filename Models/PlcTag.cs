namespace S7WpfApp.Models;

/// <summary>
/// PLC 标签/变量模型 - 由 TiaDbParser 解析生成
/// </summary>
public class PlcTag
{
    /// <summary>
    /// 完整符号名称（如 "Motor1.Speed" 或 "Data[5]"）
    /// </summary>
    public string SymbolicName { get; set; } = "";

    /// <summary>
    /// 显示名称（如 "Speed" 或 "[5]"）
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// 显示数据类型（如 "Array[0..10] of Real"）
    /// </summary>
    public string DisplayDataType { get; set; } = "";

    /// <summary>
    /// 基础数据类型（如 "Real", "Bool", "UDT", "STRUCT"）
    /// </summary>
    public string BaseDataType { get; set; } = "";

    /// <summary>
    /// 是否为容器类型（Struct/Array/UDT）
    /// </summary>
    public bool IsContainer { get; set; }

    /// <summary>
    /// 展开/折叠图标
    /// </summary>
    public string Expander { get; set; } = "";

    /// <summary>
    /// 父标签的符号名称
    /// </summary>
    public string? ParentSymbolicName { get; set; }

    /// <summary>
    /// 是否可见（用于树形展开）
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// 层级深度
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// 绝对地址（如 "DB100.DBD0"）
    /// </summary>
    public string Address { get; set; } = "";

    /// <summary>
    /// 注释/描述
    /// </summary>
    public string Comment { get; set; } = "";

    /// <summary>
    /// 获取缩进显示名称
    /// </summary>
    public string IndentedName => new string(' ', Level * 4) + DisplayName;

    /// <summary>
    /// 获取展开图标
    /// </summary>
    public string ExpandIcon => IsContainer ? (Expander == "[+]" ? "▶" : "▼") : "  ";
}
