using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using S7WpfApp.Models;
using S7WpfApp.ViewModels;

namespace S7WpfApp.Services;

/// <summary>
/// 变量解析服务 — 负责 DB 变量地址解析、UDT 成员解析、字节值解析
/// 从 DbParserViewModel 中拆分，使解析逻辑可独立测试
/// </summary>
public class VariableParserService
{
    private readonly TiaDbParser _tiaParser;

    public VariableParserService(TiaDbParser tiaParser)
    {
        _tiaParser = tiaParser;
    }

    // ═══════════════ 地址解析 ═══════════════

    /// <summary>
    /// 从地址解析字节偏移：DB100.DBD0 -> 0, DB100.DBX4.5 -> 4
    /// </summary>
    public static int ParseByteOffset(string address)
    {
        var match = Regex.Match(address, @"DB\d+\.DB[XWDLB](\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    /// <summary>
    /// 从地址解析位偏移：DB100.DBX4.5 -> 5
    /// </summary>
    public static int ParseBitOffset(string address)
    {
        var match = Regex.Match(address, @"\.(\d+)$");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    /// <summary>
    /// 从文件名和内容检测 DB 编号
    /// </summary>
    public static int DetectDbNumber(string filePath, string content)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var fileMatch = Regex.Match(fileName, @"DB[_]?(\d+)", RegexOptions.IgnoreCase);
        if (fileMatch.Success && int.TryParse(fileMatch.Groups[1].Value, out var num))
            return num;

        var contentMatch = Regex.Match(content, @"DATA_BLOCK\s+""?DB(\d+)""?", RegexOptions.IgnoreCase);
        if (contentMatch.Success && int.TryParse(contentMatch.Groups[1].Value, out var num2))
            return num2;

        return 200;
    }

    // ═══════════════ 字节值解析 ═══════════════

    /// <summary>
    /// 从字节数组中解析值（大端序 S7 格式）
    /// </summary>
    public static string ParseValueFromBytes(byte[] bytes, int offset, string dataType, int bitOffset)
    {
        try
        {
            var lower = dataType.ToLower();

            if (lower == "bool")
            {
                if (offset >= bytes.Length) return "--";
                bool value = (bytes[offset] & (1 << bitOffset)) != 0;
                return value.ToString();
            }
            if (lower == "byte")
            {
                if (offset >= bytes.Length) return "--";
                return bytes[offset].ToString();
            }
            if (lower == "char")
            {
                if (offset >= bytes.Length) return "--";
                byte b = bytes[offset];
                char c = (char)b;
                return b >= 32 && b < 127 ? $"'{c}'" : $"({b})";
            }
            if (lower == "int" || lower == "word")
            {
                if (offset + 1 >= bytes.Length) return "--";
                short value = (short)((bytes[offset] << 8) | bytes[offset + 1]);
                return value.ToString();
            }
            if (lower == "dint" || lower == "dword" || lower == "time")
            {
                if (offset + 3 >= bytes.Length) return "--";
                int value = (bytes[offset] << 24) | (bytes[offset + 1] << 16) |
                           (bytes[offset + 2] << 8) | bytes[offset + 3];
                return value.ToString();
            }
            if (lower == "real")
            {
                if (offset + 3 >= bytes.Length) return "--";
                var floatBytes = new byte[] { bytes[offset + 3], bytes[offset + 2], bytes[offset + 1], bytes[offset] };
                float value = BitConverter.ToSingle(floatBytes, 0);
                return value.ToString("F3");
            }

            return "--";
        }
        catch
        {
            return "Error";
        }
    }

    // ═══════════════ 变量子元素展开 ═══════════════

    /// <summary>
    /// 获取变量的子元素（数组展开、UDT 成员展开）
    /// </summary>
    public List<MonitorVariable> GetChildVariables(MonitorVariable parent)
    {
        var children = new List<MonitorVariable>();
        int depth = parent.Depth + 1;
        string indent = new string(' ', depth * 2);
        string parentName = parent.CleanName;

        // 检查是否是数组类型
        var arrayMatch = Regex.Match(
            parent.DataType,
            @"Array\s*\[\s*(\d+)\s*\.\.\s*(\d+)\s*\]\s*of\s+(.+)",
            RegexOptions.IgnoreCase);

        if (arrayMatch.Success)
        {
            int lower = int.Parse(arrayMatch.Groups[1].Value);
            int upper = int.Parse(arrayMatch.Groups[2].Value);
            string elemType = arrayMatch.Groups[3].Value.Trim();
            int elemSize = _tiaParser.GetPublicTypeSize(elemType);

            int currentOffset = parent.ByteOffset;
            for (int i = lower; i <= upper; i++)
            {
                bool isElemExpandable = elemType.StartsWith("\"") && elemType.EndsWith("\"");
                string childName = $"{parentName}[{i}]";
                string displayName = isElemExpandable
                    ? $"{indent}▶ {childName}"
                    : $"{indent}{childName}";

                children.Add(new MonitorVariable
                {
                    Name = childName,
                    DisplayName = displayName,
                    DataType = elemType,
                    Address = $"DB{parent.DbNumber}.DBB{currentOffset}",
                    S7Address = $"DB{parent.DbNumber}.DBB{currentOffset}",
                    DbNumber = parent.DbNumber,
                    ByteOffset = currentOffset,
                    BitOffset = 0,
                    Size = elemSize,
                    Comment = "",
                    IsArrayElement = true,
                    IsArrayParent = isElemExpandable,
                    ParentArrayName = parentName,
                    IsSelected = !isElemExpandable,
                    IsExpanded = false,
                    IsVisible = true,
                    Depth = depth
                });

                currentOffset += elemSize;
            }
        }
        // 检查是否是 UDT 类型
        else if (parent.DataType.StartsWith("\"") && parent.DataType.EndsWith("\""))
        {
            var udtName = parent.DataType.Trim('"');
            var udtDef = _tiaParser.GetUdtDefinition(udtName);
            if (udtDef != null)
            {
                var members = ParseUdtMembers(udtDef.Definition, parent.ByteOffset);

                // 如果 UDT 只有一个成员且是数组，直接展开数组元素
                if (members.Count == 1 && members[0].DataType.Contains("Array", StringComparison.OrdinalIgnoreCase))
                {
                    var arrayMember = members[0];
                    var innerArrayMatch = Regex.Match(
                        arrayMember.DataType,
                        @"Array\s*\[\s*(\d+)\s*\.\.\s*(\d+)\s*\]\s*of\s+(.+)",
                        RegexOptions.IgnoreCase);

                    if (innerArrayMatch.Success)
                    {
                        int lo = int.Parse(innerArrayMatch.Groups[1].Value);
                        int hi = int.Parse(innerArrayMatch.Groups[2].Value);
                        string elemType = innerArrayMatch.Groups[3].Value.Trim();
                        int elemSize = _tiaParser.GetPublicTypeSize(elemType);

                        int currentOffset = arrayMember.ByteOffset;
                        for (int i = lo; i <= hi; i++)
                        {
                            string childName = $"{parentName}[{i}]";
                            children.Add(new MonitorVariable
                            {
                                Name = childName,
                                DisplayName = $"{indent}{childName}",
                                DataType = elemType,
                                Address = $"DB{parent.DbNumber}.DBB{currentOffset}",
                                S7Address = $"DB{parent.DbNumber}.DBB{currentOffset}",
                                DbNumber = parent.DbNumber,
                                ByteOffset = currentOffset,
                                BitOffset = 0,
                                Size = elemSize,
                                Comment = "",
                                IsArrayElement = true,
                                IsArrayParent = false,
                                ParentArrayName = parentName,
                                IsSelected = true,
                                IsExpanded = false,
                                IsVisible = true,
                                Depth = depth
                            });
                            currentOffset += elemSize;
                        }
                    }
                }
                else
                {
                    // 正常展开 UDT 的所有成员
                    foreach (var member in members)
                    {
                        bool isExpandable = member.DataType.Contains("Array", StringComparison.OrdinalIgnoreCase) ||
                                           member.DataType.Equals("Struct", StringComparison.OrdinalIgnoreCase) ||
                                           (member.DataType.StartsWith("\"") && member.DataType.EndsWith("\""));

                        string childName = $"{parentName}.{member.Name}";
                        string displayName = isExpandable
                            ? $"{indent}▶ {childName}"
                            : $"{indent}{childName}";

                        children.Add(new MonitorVariable
                        {
                            Name = childName,
                            DisplayName = displayName,
                            DataType = member.DataType,
                            Address = $"DB{parent.DbNumber}.DBB{member.ByteOffset}",
                            S7Address = $"DB{parent.DbNumber}.DBB{member.ByteOffset}",
                            DbNumber = parent.DbNumber,
                            ByteOffset = member.ByteOffset,
                            BitOffset = member.BitOffset,
                            Size = member.Size,
                            Comment = member.Comment,
                            IsArrayElement = true,
                            IsArrayParent = isExpandable,
                            ParentArrayName = parentName,
                            IsSelected = !isExpandable,
                            IsExpanded = false,
                            IsVisible = true,
                            Depth = depth
                        });
                    }
                }
            }
        }
        // 检查是否是 Struct 类型（嵌套结构）
        else if (parent.DataType.Equals("Struct", StringComparison.OrdinalIgnoreCase))
        {
            // TODO: 需要更完整的 Struct 成员解析
        }

        return children;
    }

    /// <summary>
    /// 解析 UDT 内部成员
    /// </summary>
    public List<(string Name, string DataType, int ByteOffset, int BitOffset, int Size, string Comment)> ParseUdtMembers(string definition, int baseOffset)
    {
        var members = new List<(string Name, string DataType, int ByteOffset, int BitOffset, int Size, string Comment)>();

        var structMatch = Regex.Match(
            definition,
            @"STRUCT\s*(.*?)\s*END_STRUCT",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!structMatch.Success) return members;

        var content = structMatch.Groups[1].Value;
        var lines = content.Split('\n');
        int offset = baseOffset;
        int lineIndex = 0;

        while (lineIndex < lines.Length)
        {
            var rawLine = lines[lineIndex];
            var line = rawLine.Trim();
            lineIndex++;

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;
            if (line.StartsWith("END_STRUCT", StringComparison.OrdinalIgnoreCase)) continue;

            // 检查是否是嵌套 Struct
            if (line.Contains(": Struct", StringComparison.OrdinalIgnoreCase))
            {
                var colonIdx = line.IndexOf(':');
                if (colonIdx > 0)
                {
                    var sName = line.Substring(0, colonIdx).Trim().Trim('"');
                    int nestedDepth = 1;
                    var nestedLines = new List<string>();
                    while (lineIndex < lines.Length && nestedDepth > 0)
                    {
                        var nl = lines[lineIndex].Trim();
                        lineIndex++;
                        if (nl.Contains(": Struct", StringComparison.OrdinalIgnoreCase)) nestedDepth++;
                        if (nl.StartsWith("END_STRUCT", StringComparison.OrdinalIgnoreCase)) nestedDepth--;
                        if (nestedDepth > 0) nestedLines.Add(nl);
                    }
                    int sSize = 0;
                    foreach (var nl in nestedLines)
                    {
                        var cl = Regex.Replace(nl, @"\s*\{[^}]*\}", "").Trim();
                        var ci = cl.IndexOf(':');
                        if (ci > 0) sSize += _tiaParser.GetPublicTypeSize(cl.Substring(ci + 1).Trim());
                    }
                    if (sSize % 2 != 0) sSize++;
                    if (sSize >= 2 && offset % 2 != 0) offset++;
                    members.Add((sName, "Struct", offset, 0, sSize, $"[嵌套]"));
                    offset += sSize;
                }
                continue;
            }

            // 移除注释
            var commentMatch = Regex.Match(line, @"//\s*(.*)$");
            var comment = commentMatch.Success ? commentMatch.Groups[1].Value.Trim() : "";
            var cleanLine = Regex.Replace(line, @"//.*$", "").Trim().TrimEnd(';');
            cleanLine = Regex.Replace(cleanLine, @"\s*\{[^}]*\}", "");

            var colonIndex = cleanLine.IndexOf(':');
            if (colonIndex < 0) continue;

            var varName = cleanLine.Substring(0, colonIndex).Trim().Trim('"');
            var typeStr = cleanLine.Substring(colonIndex + 1).Trim();
            var eqIndex = typeStr.IndexOf(":=");
            if (eqIndex > 0) typeStr = typeStr.Substring(0, eqIndex).Trim();

            if (string.IsNullOrWhiteSpace(varName)) continue;

            int varSize = _tiaParser.GetPublicTypeSize(typeStr);

            // 偶数对齐
            if (varSize >= 2 && offset % 2 != 0)
            {
                offset++;
            }

            members.Add((varName, typeStr, offset, 0, varSize, comment));
            offset += varSize;
        }

        return members;
    }
}
