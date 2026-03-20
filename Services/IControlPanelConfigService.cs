using System.Threading.Tasks;
using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// 控制面板配置导入导出服务接口
/// </summary>
public interface IControlPanelConfigService
{
    /// <summary>
    /// 导出完整配置（控件绑定 + 轴配置 + 符号表）
    /// </summary>
    (string json, int bindingCount, int axisCount, int symbolCount) BuildExportConfig();

    /// <summary>
    /// 导入配置文件
    /// </summary>
    Task<(int bindings, int axes, int symbols)> ImportConfigAsync(string filePath);

    /// <summary>
    /// 按数据类型分派读取 PLC 值
    /// </summary>
    Task<object?> ReadValueAsync(ControlBinding binding);

    /// <summary>
    /// 按数据类型分派写入 PLC 值
    /// </summary>
    Task WriteTypedAsync(ControlBinding binding, object value);
}
