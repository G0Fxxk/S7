namespace S7WpfApp.Models;

/// <summary>
/// 配方变量定义（配方表的列定义）
/// </summary>
public class RecipeVariableDefinition
{
    /// <summary>
    /// 变量名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// PLC 绝对地址（如 DB200.DBW0）
    /// </summary>
    public string Address { get; set; } = "";

    /// <summary>
    /// 符号名称（可选，优先通过符号解析地址）
    /// </summary>
    public string SymbolName { get; set; } = "";

    /// <summary>
    /// 数据类型（Bool/Byte/Char/Int/DInt/Real）
    /// </summary>
    public string DataType { get; set; } = "Int";

    /// <summary>
    /// 备注
    /// </summary>
    public string Comment { get; set; } = "";
}

/// <summary>
/// 配方数据集 - 同一配方表下不同产品/工艺的参数值
/// </summary>
public class RecipeDataSet
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 数据集名称（如 "产品A"、"产品B"）
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 变量值字典：变量名 → 值（字符串表示）
    /// </summary>
    public Dictionary<string, string> Values { get; set; } = new();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 修改时间
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 配方表 - 定义一组变量及其多个数据集
/// </summary>
public class RecipeTable
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 配方表名称（如 "配料配方"）
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// 变量定义列表
    /// </summary>
    public List<RecipeVariableDefinition> Variables { get; set; } = new();

    /// <summary>
    /// 配方数据集列表
    /// </summary>
    public List<RecipeDataSet> DataSets { get; set; } = new();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 配方持久化配置
/// </summary>
public class RecipeConfiguration
{
    /// <summary>
    /// 版本
    /// </summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// 所有配方表
    /// </summary>
    public List<RecipeTable> Tables { get; set; } = new();
}
