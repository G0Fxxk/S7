using System.IO;

namespace S7WpfApp.Services;

/// <summary>
/// 配方操作日志服务 - 记录配方的创建、修改、读写等操作
/// </summary>
public class RecipeLogService : IRecipeLogService
{
    private readonly string _logDirectory;
    private readonly object _lock = new();

    /// <summary>
    /// 日志消息事件（用于 UI 实时显示）
    /// </summary>
    public event EventHandler<string>? LogAdded;

    public RecipeLogService()
    {
        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "S7WpfApp", "logs");
        Directory.CreateDirectory(_logDirectory);
    }

    /// <summary>
    /// 记录配方操作日志
    /// </summary>
    /// <param name="operation">操作类型（创建/修改/删除/读取/写入/导入/导出）</param>
    /// <param name="tableName">配方表名</param>
    /// <param name="dataSetName">数据集名（可选）</param>
    /// <param name="detail">详细信息</param>
    public void Log(string operation, string tableName, string? dataSetName = null, string? detail = null)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var dataSetPart = string.IsNullOrEmpty(dataSetName) ? "" : $" [{dataSetName}]";
        var detailPart = string.IsNullOrEmpty(detail) ? "" : $" - {detail}";
        var logLine = $"[{timestamp}] [{operation}] {tableName}{dataSetPart}{detailPart}";

        // 写入文件
        lock (_lock)
        {
            try
            {
                var logFile = Path.Combine(_logDirectory, $"recipe_log_{DateTime.Now:yyyy-MM-dd}.txt");
                File.AppendAllText(logFile, logLine + Environment.NewLine);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"写入配方日志失败: {ex.Message}");
            }
        }

        // 触发事件（UI 显示）
        LogAdded?.Invoke(this, logLine);
    }

    /// <summary>
    /// 获取今日日志内容
    /// </summary>
    public string GetTodayLog()
    {
        try
        {
            var logFile = Path.Combine(_logDirectory, $"recipe_log_{DateTime.Now:yyyy-MM-dd}.txt");
            return File.Exists(logFile) ? File.ReadAllText(logFile) : "";
        }
        catch
        {
            return "";
        }
    }
}
