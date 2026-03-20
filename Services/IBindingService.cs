using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// 控件绑定服务接口
/// </summary>
public interface IBindingService
{
    /// <summary>
    /// 获取所有绑定配置
    /// </summary>
    List<ControlBinding> GetBindings();

    /// <summary>
    /// 获取指定分组的绑定
    /// </summary>
    List<ControlBinding> GetBindingsByGroup(string group);

    /// <summary>
    /// 获取所有分组名称
    /// </summary>
    List<string> GetGroups();

    /// <summary>
    /// 保存绑定配置
    /// </summary>
    void SaveBinding(ControlBinding binding);

    /// <summary>
    /// 更新绑定配置
    /// </summary>
    void UpdateBinding(ControlBinding binding);

    /// <summary>
    /// 删除绑定
    /// </summary>
    void RemoveBinding(string address);

    /// <summary>
    /// 根据地址获取绑定
    /// </summary>
    ControlBinding? GetBindingByAddress(string address);

    /// <summary>
    /// 导出配置到文件
    /// </summary>
    Task<string> ExportAsync(string filePath);

    /// <summary>
    /// 从文件导入配置
    /// </summary>
    Task ImportAsync(string filePath);

    /// <summary>
    /// 清空所有绑定
    /// </summary>
    void ClearAll();

    /// <summary>
    /// 绑定变更事件
    /// </summary>
    event EventHandler? BindingsChanged;
}
