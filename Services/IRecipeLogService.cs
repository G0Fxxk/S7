using System;

namespace S7WpfApp.Services;

/// <summary>
/// 配方操作日志服务接口
/// </summary>
public interface IRecipeLogService
{
    /// <summary>
    /// 日志消息事件（用于 UI 实时显示）
    /// </summary>
    event EventHandler<string>? LogAdded;

    /// <summary>
    /// 记录配方操作日志
    /// </summary>
    void Log(string operation, string tableName, string? dataSetName = null, string? detail = null);

    /// <summary>
    /// 获取今日日志内容
    /// </summary>
    string GetTodayLog();
}
