using System.Collections.Generic;
using System.Threading.Tasks;
using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// PLC 数据批量读写服务接口
/// </summary>
public interface IPlcDataReadWriteService
{
    /// <summary>
    /// 批量读取选中变量（分段优化）
    /// </summary>
    /// <returns>(totalRequests, elapsedMs) 元组</returns>
    Task<(int totalRequests, long elapsedMs)> ReadVariablesAsync(IReadOnlyList<MonitorVariable> variables);

    /// <summary>
    /// 写入单个变量到 PLC
    /// </summary>
    Task WriteVariableAsync(MonitorVariable variable, string writeValue);
}
