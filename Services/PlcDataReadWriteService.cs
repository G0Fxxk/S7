using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// PLC 数据批量读写服务 — 从 DbParserViewModel 中提取的核心读写逻辑
/// </summary>
public class PlcDataReadWriteService : IPlcDataReadWriteService
{
    private readonly IPlcService _plcService;

    public PlcDataReadWriteService(IPlcService plcService)
    {
        _plcService = plcService;
    }

    /// <summary>
    /// 批量读取选中变量（分段优化）
    /// </summary>
    /// <param name="variables">需要读取的变量列表（已过滤，不含数组父节点）</param>
    /// <returns>(totalRequests, elapsedMs) 元组</returns>
    public async Task<(int totalRequests, long elapsedMs)> ReadVariablesAsync(
        IReadOnlyList<MonitorVariable> variables)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int totalRequests = 0;

        // 按 DB 编号分组
        var dbGroups = variables.GroupBy(x => x.DbNumber).ToList();

        const int MAX_CHUNK_SIZE = 200;  // S7 PDU 限制约 200 字节

        foreach (var group in dbGroups)
        {
            int dbNumber = group.Key;
            var vars = group.OrderBy(v => v.ByteOffset).ToList();

            // 将变量分成连续的数据段（每段最多 200 字节）
            var segments = BuildSegments(vars, MAX_CHUNK_SIZE);

            // 读取每个数据段
            foreach (var (start, length, segmentVars) in segments)
            {
                try
                {
                    byte[] data = await _plcService.ReadBytesAsync(dbNumber, start, length);
                    totalRequests++;

                    // 在本地解析每个变量的值
                    foreach (var v in segmentVars)
                    {
                        try
                        {
                            int relativeOffset = v.ByteOffset - start;
                            v.CurrentValue = ParseVariableValue(data, relativeOffset, v);
                        }
                        catch (Exception ex) { v.CurrentValue = "Error"; System.Diagnostics.Debug.WriteLine($"变量解析异常 [{v.Name}]: {ex.Message}"); }
                    }
                }
                catch
                {
                    foreach (var v in segmentVars) v.CurrentValue = "Error";
                }
            }
        }

        sw.Stop();
        return (totalRequests, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// 写入单个变量到 PLC
    /// </summary>
    public async Task WriteVariableAsync(MonitorVariable variable, string writeValue)
    {
        // 处理字符串类型
        if (variable.IsString)
        {
            var match = Regex.Match(variable.DataType, @"String\s*\[\s*(\d+)\s*\]", RegexOptions.IgnoreCase);
            int maxLen = match.Success ? int.Parse(match.Groups[1].Value) : 254;
            await _plcService.WriteStringAsync(variable.DbNumber, variable.ByteOffset, writeValue, maxLen);
        }
        // 处理字符数组类型 (Array[1..40] of Char)
        else if (variable.IsCharArray)
        {
            var bytes = Encoding.ASCII.GetBytes(writeValue);
            if (bytes.Length > variable.Size)
            {
                bytes = bytes.Take(variable.Size).ToArray();
            }
            else if (bytes.Length < variable.Size)
            {
                var paddedBytes = new byte[variable.Size];
                Array.Copy(bytes, paddedBytes, bytes.Length);
                bytes = paddedBytes;
            }
            await _plcService.WriteBytesAsync(variable.DbNumber, variable.ByteOffset, bytes);
        }
        // 处理基本类型 - 使用智能写入
        else
        {
            object? valueToWrite = variable.DataType.ToLower() switch
            {
                "bool" => writeValue.ToLower() == "true" || writeValue == "1",
                "byte" => byte.TryParse(writeValue, out var byteVal) ? byteVal : null,
                "char" => !string.IsNullOrEmpty(writeValue) ? (byte)writeValue[0] : null,
                "int" or "word" => short.TryParse(writeValue, out var intVal) ? intVal : null,
                "dint" or "dword" or "time" => int.TryParse(writeValue, out var dintVal) ? dintVal : null,
                "real" => float.TryParse(writeValue, out var realVal) ? realVal : null,
                _ => null
            };

            if (valueToWrite == null)
                throw new InvalidOperationException($"不支持写入类型或值无效: {variable.DataType}");

            // 针对 Char / Byte 类型走 WriteAsync 确保装箱无误
            if (variable.DataType.Equals("Char", StringComparison.OrdinalIgnoreCase) ||
                variable.DataType.Equals("Byte", StringComparison.OrdinalIgnoreCase))
            {
                var byteValue = Convert.ToByte(valueToWrite);
                await _plcService.WriteAsync(variable.S7Address, byteValue);
            }
            else
            {
                await _plcService.WriteAutoAsync(variable.S7Address, valueToWrite);
            }
        }
    }

    // ═══════════════ 内部方法 ═══════════════

    /// <summary>
    /// 将变量列表按字节偏移分成连续的数据段
    /// </summary>
    private static List<(int start, int length, List<MonitorVariable> vars)> BuildSegments(
        List<MonitorVariable> vars, int maxChunkSize)
    {
        var segments = new List<(int, int, List<MonitorVariable>)>();
        if (vars.Count == 0) return segments;

        int segStart = vars[0].ByteOffset;
        int segEnd = vars[0].ByteOffset + vars[0].Size;
        var currentSegVars = new List<MonitorVariable> { vars[0] };

        for (int i = 1; i < vars.Count; i++)
        {
            var v = vars[i];
            int vEnd = v.ByteOffset + v.Size;

            // 如果当前变量与段连续（允许50字节间隙），且段长度不超过限制，则合并
            if (v.ByteOffset <= segEnd + 50 && (vEnd - segStart) <= maxChunkSize)
            {
                segEnd = Math.Max(segEnd, vEnd);
                currentSegVars.Add(v);
            }
            else
            {
                segments.Add((segStart, segEnd - segStart, currentSegVars));
                segStart = v.ByteOffset;
                segEnd = vEnd;
                currentSegVars = new List<MonitorVariable> { v };
            }
        }
        segments.Add((segStart, segEnd - segStart, currentSegVars));

        return segments;
    }

    /// <summary>
    /// 从字节缓冲区中解析变量值
    /// </summary>
    private static string ParseVariableValue(byte[] data, int relativeOffset, MonitorVariable v)
    {
        // 处理字符数组类型 - 显示为字符串
        if (v.IsCharArray)
        {
            int endOffset = Math.Min(relativeOffset + v.Size, data.Length);
            int len = endOffset - relativeOffset;
            if (len > 0)
            {
                var charBytes = new byte[len];
                Array.Copy(data, relativeOffset, charBytes, 0, len);
                return Encoding.ASCII.GetString(charBytes).TrimEnd('\0');
            }
            return "";
        }

        if (v.IsByteArray)
        {
            int endOffset = Math.Min(relativeOffset + v.Size, data.Length);
            int len = endOffset - relativeOffset;
            if (len > 0)
            {
                var byteArr = new byte[len];
                Array.Copy(data, relativeOffset, byteArr, 0, len);
                return BitConverter.ToString(byteArr).Replace("-", " ");
            }
            return "";
        }

        if (v.IsString)
        {
            if (relativeOffset + 2 < data.Length)
            {
                int actualLen = data[relativeOffset + 1];
                int strStart = relativeOffset + 2;
                int strEnd = Math.Min(strStart + actualLen, data.Length);
                int len = strEnd - strStart;
                if (len > 0)
                {
                    var strBytes = new byte[len];
                    Array.Copy(data, strStart, strBytes, 0, len);
                    return Encoding.ASCII.GetString(strBytes);
                }
            }
            return "";
        }

        return VariableParserService.ParseValueFromBytes(data, relativeOffset, v.DataType, v.BitOffset);
    }
}
