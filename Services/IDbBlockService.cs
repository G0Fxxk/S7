using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// DB 块管理服务接口
/// </summary>
public interface IDbBlockService
{
    /// <summary>
    /// 获取所有已配置的 DB 块
    /// </summary>
    List<DbBlock> GetAllDbBlocks();

    /// <summary>
    /// 添加 DB 块
    /// </summary>
    void AddDbBlock(DbBlock dbBlock);

    /// <summary>
    /// 移除 DB 块
    /// </summary>
    void RemoveDbBlock(int dbNumber);

    /// <summary>
    /// 获取指定的 DB 块
    /// </summary>
    DbBlock? GetDbBlock(int dbNumber);

    /// <summary>
    /// 更新 DB 块
    /// </summary>
    void UpdateDbBlock(DbBlock dbBlock);

    /// <summary>
    /// 添加变量到 DB 块
    /// </summary>
    void AddVariable(int dbNumber, DbVariable variable);

    /// <summary>
    /// 从 DB 块移除变量
    /// </summary>
    void RemoveVariable(int dbNumber, string variableName);

    /// <summary>
    /// 获取所有已选中的变量（用于监控）
    /// </summary>
    List<(DbBlock Db, DbVariable Variable)> GetSelectedVariables();

    /// <summary>
    /// 导出配置到 JSON 文件
    /// </summary>
    Task<string> ExportToJsonAsync(string filePath);

    /// <summary>
    /// 从 JSON 文件导入配置
    /// </summary>
    Task<bool> ImportFromJsonAsync(string filePath);

    /// <summary>
    /// 从 TIA Portal DB 文件导入（SCL/XML格式）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="dbNumber">可选的 DB 编号，如果为 null 则从文件解析</param>
    /// <returns>导入的 DB 块</returns>
    Task<DbBlock?> ImportFromDbFileAsync(string filePath, int? dbNumber = null);

    /// <summary>
    /// 保存配置到本地存储
    /// </summary>
    Task SaveConfigurationAsync();

    /// <summary>
    /// 从本地存储加载配置
    /// </summary>
    Task LoadConfigurationAsync();

    /// <summary>
    /// 创建常用的预设 DB 块模板
    /// </summary>
    DbBlock CreatePresetDbBlock(string presetName, int dbNumber);

    /// <summary>
    /// 选中/取消选中 DB 块中的所有变量
    /// </summary>
    void SelectAllVariables(int dbNumber, bool selected);
}

