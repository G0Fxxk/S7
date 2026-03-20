namespace S7WpfApp.Models;

/// <summary>
/// 轴类型
/// </summary>
public enum AxisType
{
    Linear,  // 线性轴（有正负限位+原点）
    Rotary   // 旋转轴（只有原点）
}

/// <summary>
/// 轴控制配置
/// </summary>
public class AxisConfig
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 轴名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 分组名称
    /// </summary>
    public string Group { get; set; } = "";

    /// <summary>
    /// DB 编号
    /// </summary>
    public int DbNumber { get; set; }

    /// <summary>
    /// 轴类型（线性 / 旋转）
    /// </summary>
    public AxisType AxisType { get; set; } = AxisType.Linear;

    // ========== 地址配置 ==========

    /// <summary>
    /// 当前位置地址 (Real)
    /// </summary>
    public string CurrentPositionAddress { get; set; } = "";

    /// <summary>
    /// 轴使能地址 (Bool)
    /// </summary>
    public string ServoEnableAddress { get; set; } = "";

    /// <summary>
    /// 后退地址 (Bool)
    /// </summary>
    public string BackwardAddress { get; set; } = "";

    /// <summary>
    /// 前进地址 (Bool)
    /// </summary>
    public string ForwardAddress { get; set; } = "";

    /// <summary>
    /// 回原位地址 (Bool)
    /// </summary>
    public string HomeAddress { get; set; } = "";

    /// <summary>
    /// 点动速度地址 (Real)
    /// </summary>
    public string JogSpeedAddress { get; set; } = "";

    /// <summary>
    /// 手动定位速度地址 (Real)
    /// </summary>
    public string ManualSpeedAddress { get; set; } = "";

    /// <summary>
    /// 手动定位位置地址 (Real)
    /// </summary>
    public string ManualPositionAddress { get; set; } = "";

    /// <summary>
    /// 手动定位触发地址 (Bool)
    /// </summary>
    public string ManualTriggerAddress { get; set; } = "";

    /// <summary>
    /// 负限位地址 (Bool)
    /// </summary>
    public string NegativeLimitAddress { get; set; } = "";

    /// <summary>
    /// 正限位地址 (Bool)
    /// </summary>
    public string PositiveLimitAddress { get; set; } = "";

    /// <summary>
    /// 原点地址 (Bool)
    /// </summary>
    public string OriginAddress { get; set; } = "";
}
