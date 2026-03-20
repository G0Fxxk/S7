namespace S7WpfApp.Models;

/// <summary>
/// 控件绑定类型
/// </summary>
public enum BindingType
{
    /// <summary>
    /// 无绑定
    /// </summary>
    None = 0,

    /// <summary>
    /// 点动按钮 - 按下写True，松开写False
    /// </summary>
    MomentaryButton = 1,

    /// <summary>
    /// 保持按钮 - 点击切换True/False
    /// </summary>
    ToggleButton = 2,

    /// <summary>
    /// 数值输入 - 输入并写入Int/Real
    /// </summary>
    NumericInput = 3,

    /// <summary>
    /// 状态指示 - 只读显示当前值
    /// </summary>
    StatusIndicator = 4
}

/// <summary>
/// 控件绑定配置
/// </summary>
public class ControlBinding
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// PLC 地址 (如 DB200.DBX0.0)
    /// </summary>
    public string Address { get; set; } = "";

    /// <summary>
    /// 绑定类型
    /// </summary>
    public BindingType Type { get; set; } = BindingType.None;

    /// <summary>
    /// 控件显示名称 (如 "启动按钮")
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 分组名称 (如 "手动操作")
    /// </summary>
    public string Group { get; set; } = "默认";

    /// <summary>
    /// 数据类型 (Bool/Int/DInt/Real/String)
    /// </summary>
    public string DataType { get; set; } = "Bool";

    /// <summary>
    /// 字符串最大长度（仅 DataType=String 时有效，对应 S7 String 的 MaxLength）
    /// </summary>
    public int StringMaxLength { get; set; } = 254;

    /// <summary>
    /// DB 编号
    /// </summary>
    public int DbNumber { get; set; }

    /// <summary>
    /// 排序顺序
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 绑定的符号名称（用于动态地址更新）
    /// </summary>
    public string SymbolName { get; set; } = "";
}

/// <summary>
/// 绑定配置集合（用于导出/导入）
/// </summary>
public class BindingConfiguration
{
    /// <summary>
    /// 配置版本
    /// </summary>
    public string Version { get; set; } = "2.0";

    /// <summary>
    /// 导出时间
    /// </summary>
    public DateTime ExportTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 控件绑定列表
    /// </summary>
    public List<ControlBinding> Bindings { get; set; } = new();

    /// <summary>
    /// 轴配置列表（v2.0 新增）
    /// </summary>
    public List<Models.AxisConfig>? AxisConfigs { get; set; }

    /// <summary>
    /// 符号表（v2.0 新增）
    /// </summary>
    public List<Services.SymbolEntry>? Symbols { get; set; }
}
