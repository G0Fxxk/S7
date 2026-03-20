using System.Text.Json.Serialization;

namespace S7WpfApp.Services;

/// <summary>
/// 符号服务接口 - 提供符号变量寻址功能
/// </summary>
public interface ISymbolService
{
    /// <summary>
    /// 根据符号名称获取绝对地址
    /// </summary>
    string? GetAddress(string symbolName);

    /// <summary>
    /// 根据绝对地址获取符号名称
    /// </summary>
    string? GetSymbolName(string address);

    /// <summary>
    /// 获取所有符号条目
    /// </summary>
    IEnumerable<SymbolEntry> GetAllSymbols();

    /// <summary>
    /// 获取所有分类名称
    /// </summary>
    IEnumerable<string> GetCategories();

    /// <summary>
    /// 获取指定分类下的符号
    /// </summary>
    IEnumerable<SymbolEntry> GetSymbolsByCategory(string category);

    /// <summary>
    /// 搜索符号（按名称、地址或注释模糊匹配）
    /// </summary>
    IEnumerable<SymbolEntry> SearchSymbols(string keyword);

    /// <summary>
    /// 添加符号
    /// </summary>
    /// <param name="name">自定义符号名</param>
    /// <param name="address">当前绝对地址</param>
    /// <param name="dataType">数据类型</param>
    /// <param name="originalSymbolPath">DB 中的原始符号路径（用于地址更新）</param>
    /// <param name="dbNumber">DB 编号</param>
    /// <param name="dbFilePath">DB 源文件路径（用于自动更新）</param>
    /// <param name="comment">注释</param>
    void AddSymbol(string name, string address, string dataType, string originalSymbolPath = "", int dbNumber = 0, string dbFilePath = "", string comment = "", string category = "");

    /// <summary>
    /// 更新符号
    /// </summary>
    void UpdateSymbol(string oldName, string newName, string address, string dataType, string comment = "");

    /// <summary>
    /// 删除符号
    /// </summary>
    void RemoveSymbol(string name);

    /// <summary>
    /// 检查符号名是否存在
    /// </summary>
    bool SymbolExists(string name);

    /// <summary>
    /// 检查地址是否已有符号关联
    /// </summary>
    bool AddressHasSymbol(string address);

    /// <summary>
    /// 从 DbBlockService 刷新/合并符号（可选）
    /// </summary>
    void RefreshFromDbBlocks();

    /// <summary>
    /// 根据新解析的 DB 数据更新符号地址
    /// </summary>
    /// <param name="dbNumber">DB 编号</param>
    /// <param name="tags">新解析的标签列表</param>
    /// <returns>更新的符号数量</returns>
    int UpdateAddressesFromParsedTags(int dbNumber, List<Models.PlcTag> tags);

    /// <summary>
    /// 保存符号配置到文件
    /// </summary>
    Task SaveAsync();

    /// <summary>
    /// 加载符号配置
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// 符号表变更事件
    /// </summary>
    event EventHandler? SymbolTableChanged;
}

/// <summary>
/// 符号条目 - 表示符号表中的一条记录
/// </summary>
public class SymbolEntry
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 自定义符号名称（如 "电机1速度"）
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 对应的绝对地址（如 "DB100.DBD0"）
    /// </summary>
    public string Address { get; set; } = "";

    /// <summary>
    /// 数据类型
    /// </summary>
    public string DataType { get; set; } = "";

    /// <summary>
    /// 注释
    /// </summary>
    public string Comment { get; set; } = "";

    /// <summary>
    /// DB 中的原始符号路径（如 "Motor1.Speed"）- 用于地址更新
    /// </summary>
    public string OriginalSymbolPath { get; set; } = "";

    /// <summary>
    /// 所属 DB 编号 - 用于地址更新
    /// </summary>
    public int DbNumber { get; set; }

    /// <summary>
    /// 关联的 DB 源文件路径 - 用于自动地址更新
    /// </summary>
    public string DbFilePath { get; set; } = "";

    /// <summary>
    /// 分类名称（如 "DB103.HMI控制数据"）
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// 用于符号浏览器显示
    /// </summary>
    [JsonIgnore]
    public string SymbolPath => Name;
}

/// <summary>
/// 符号配置文件模型
/// </summary>
public class SymbolConfiguration
{
    public string Version { get; set; } = "1.0";
    public DateTime LastModified { get; set; } = DateTime.Now;
    public List<SymbolEntry> Symbols { get; set; } = new();
}
