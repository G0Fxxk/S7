using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// 控制面板配置导入导出服务 + PLC 读写分派 — 从 ControlPanelViewModel 中提取
/// </summary>
public class ControlPanelConfigService : IControlPanelConfigService
{
    private readonly IPlcService _plcService;
    private readonly IBindingService _bindingService;
    private readonly IAxisConfigService _axisConfigService;
    private readonly ISymbolService _symbolService;

    public ControlPanelConfigService(
        IPlcService plcService,
        IBindingService bindingService,
        IAxisConfigService axisConfigService,
        ISymbolService symbolService)
    {
        _plcService = plcService;
        _bindingService = bindingService;
        _axisConfigService = axisConfigService;
        _symbolService = symbolService;
    }

    // ═══════════════ 配置导入导出 ═══════════════

    /// <summary>
    /// 导出完整配置（控件绑定 + 轴配置 + 符号表）
    /// </summary>
    /// <returns>序列化后的 JSON 字符串</returns>
    public (string json, int bindingCount, int axisCount, int symbolCount) BuildExportConfig()
    {
        var config = new BindingConfiguration
        {
            Version = "2.0",
            ExportTime = DateTime.Now,
            Bindings = _bindingService.GetBindings(),
            AxisConfigs = _axisConfigService.GetAll(),
            Symbols = _symbolService.GetAllSymbols().ToList()
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(config, options);

        return (json,
            config.Bindings.Count,
            config.AxisConfigs?.Count ?? 0,
            config.Symbols?.Count ?? 0);
    }

    /// <summary>
    /// 导入配置文件
    /// </summary>
    /// <returns>(bindingCount, axisCount, symbolCount) 实际导入数量</returns>
    public async Task<(int bindings, int axes, int symbols)> ImportConfigAsync(string filePath)
    {
        var json = await System.IO.File.ReadAllTextAsync(filePath);
        var config = JsonSerializer.Deserialize<BindingConfiguration>(json)
            ?? throw new InvalidOperationException("无效的配置文件");

        // 1. 导入控件绑定
        int bindingCount = config.Bindings?.Count ?? 0;
        if (config.Bindings != null)
        {
            await _bindingService.ImportAsync(filePath);
        }

        // 2. 导入轴配置
        int axisCount = 0;
        if (config.AxisConfigs != null)
        {
            foreach (var axis in config.AxisConfigs)
            {
                _axisConfigService.Save(axis);
                axisCount++;
            }
        }

        // 3. 导入符号表
        int symCount = 0;
        if (config.Symbols != null)
        {
            foreach (var sym in config.Symbols)
            {
                if (!_symbolService.SymbolExists(sym.Name))
                {
                    _symbolService.AddSymbol(sym.Name, sym.Address, sym.DataType,
                        sym.OriginalSymbolPath, sym.DbNumber, sym.DbFilePath, sym.Comment, sym.Category);
                    symCount++;
                }
            }
            await _symbolService.SaveAsync();
        }

        return (bindingCount, axisCount, symCount);
    }

    // ═══════════════ PLC 读写分派 ═══════════════

    /// <summary>
    /// 按数据类型分派读取 PLC 值
    /// </summary>
    public async Task<object?> ReadValueAsync(ControlBinding binding)
    {
        var dt = binding.DataType?.ToLower() ?? "";
        var addr = binding.Address;

        try
        {
            if (dt == "string")
            {
                int startByte = ParseStartByte(addr);
                return await _plcService.ReadStringAsync(binding.DbNumber, startByte, binding.StringMaxLength);
            }

            return dt switch
            {
                "bool" => await _plcService.ReadAsync<bool>(addr),
                "byte" => await _plcService.ReadAsync<byte>(addr),
                "char" => (await _plcService.ReadAsync<byte>(addr)) is byte b && b != 0 ? ((char)b).ToString() : "",
                "int" => await _plcService.ReadAsync<short>(addr),
                "dint" => await _plcService.ReadAsync<int>(addr),
                "real" => await _plcService.ReadAsync<float>(addr),
                _ => await _plcService.ReadAutoAsync(addr)
            };
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"读PLC值异常 [{binding.Address}]: {ex.Message}"); return null; }
    }

    /// <summary>
    /// 按数据类型分派写入 PLC 值
    /// </summary>
    public async Task WriteTypedAsync(ControlBinding binding, object value)
    {
        var dt = binding.DataType?.ToLower() ?? "";
        var addr = binding.Address;

        switch (dt)
        {
            case "bool":
                await _plcService.WriteAsync(addr, Convert.ToBoolean(value));
                break;
            case "byte":
                await _plcService.WriteAsync(addr, Convert.ToByte(value));
                break;
            case "char":
                var charStr = value?.ToString();
                byte charByte = string.IsNullOrEmpty(charStr) ? (byte)0 : (byte)charStr[0];
                await _plcService.WriteAsync(addr, charByte);
                break;
            case "int":
                await _plcService.WriteAsync(addr, Convert.ToInt16(value));
                break;
            case "dint":
                await _plcService.WriteAsync(addr, Convert.ToInt32(value));
                break;
            case "real":
                await _plcService.WriteAsync(addr, Convert.ToSingle(value));
                break;
            case "string":
                int startByte = ParseStartByte(addr);
                await _plcService.WriteStringAsync(binding.DbNumber, startByte, value?.ToString() ?? "", binding.StringMaxLength);
                break;
            default:
                await _plcService.WriteAutoAsync(addr, value);
                break;
        }
    }

    /// <summary>
    /// 从地址字符串中解析起始字节偏移量（如 DB200.DBB100 → 100）
    /// </summary>
    private static int ParseStartByte(string address)
    {
        address = address.ToUpper().Trim();
        if (address.StartsWith("DB"))
        {
            // 格式: DB<n>.DB[XBWD]<offset>  或  DB<n>.DB<offset>
            var dotIdx = address.IndexOf('.');
            if (dotIdx >= 0)
            {
                var offsetPart = address.Substring(dotIdx + 1);
                // 去掉 "DB" 前缀和类型字母（X/B/W/D）
                if (offsetPart.StartsWith("DB"))
                    offsetPart = offsetPart.Substring(2);
                // 去掉类型字母
                while (offsetPart.Length > 0 && !char.IsDigit(offsetPart[0]))
                    offsetPart = offsetPart.Substring(1);
                // 截取到下一个点（如果有 .bit 部分）
                var nextDot = offsetPart.IndexOf('.');
                if (nextDot >= 0) offsetPart = offsetPart.Substring(0, nextDot);
                if (int.TryParse(offsetPart, out int offset))
                    return offset;
            }
        }
        return 0;
    }
}
