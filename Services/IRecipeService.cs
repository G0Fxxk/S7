using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// 配方服务接口
/// </summary>
public interface IRecipeService
{
    /// <summary>
    /// 获取所有配方表
    /// </summary>
    List<RecipeTable> GetTables();

    /// <summary>
    /// 保存配方表（新增或更新）
    /// </summary>
    void SaveTable(RecipeTable table);

    /// <summary>
    /// 删除配方表
    /// </summary>
    void DeleteTable(string tableId);

    /// <summary>
    /// 添加数据集到配方表
    /// </summary>
    RecipeDataSet AddDataSet(RecipeTable table, string name);

    /// <summary>
    /// 删除数据集
    /// </summary>
    void DeleteDataSet(RecipeTable table, string dataSetId);

    /// <summary>
    /// 从 PLC 读取当前值到数据集
    /// </summary>
    Task ReadFromPlcAsync(RecipeTable table, RecipeDataSet dataSet);

    /// <summary>
    /// 将数据集值写入 PLC
    /// </summary>
    Task WriteToPlcAsync(RecipeTable table, RecipeDataSet dataSet);

    /// <summary>
    /// 导出配方表为 CSV（含所有数据集）
    /// </summary>
    Task ExportToCsvAsync(RecipeTable table, string filePath);

    /// <summary>
    /// 从 CSV 导入配方表
    /// </summary>
    Task<RecipeTable> ImportFromCsvAsync(string filePath);

    /// <summary>
    /// 解析符号地址（通过 SymbolService 将符号名转为绝对地址）
    /// </summary>
    void ResolveSymbolAddresses(RecipeTable table);

    /// <summary>
    /// 保存所有数据到文件
    /// </summary>
    void SaveAll();
}
