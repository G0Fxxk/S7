using System.IO;
using System.Text.RegularExpressions;
using S7WpfApp.Models;

namespace S7WpfApp.Services;

/// <summary>
/// TIA Portal DB 源文件解析器 - 计算非优化 DB 的绝对地址
/// 支持：基本类型、String[N]、Array、UDT、嵌套 STRUCT
/// </summary>
public class TiaDbParser
{
    // 内部状态
    private List<PlcTag> _allTags = new();
    private Dictionary<string, List<(string Name, string DataType, string Comment)>> _udtDefinitions = new();
    private int _dbNumber;
    private string _parsedDbName = "";

    // 公共属性
    public string ParsedDbName => _parsedDbName;
    public int DbNumber => _dbNumber;
    public List<PlcTag> AllTags => _allTags;

    /// <summary>
    /// 解析 TIA Portal .db 源文件
    /// </summary>
    /// <param name="content">文件内容</param>
    /// <param name="dbNumber">DB 编号（如未指定则从文件名或默认值推断）</param>
    public List<PlcTag> Parse(string content, int dbNumber = 100)
    {
        _allTags.Clear();
        _udtDefinitions.Clear();
        _parsedDbName = "";
        _dbNumber = dbNumber;

        var lines = content.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        // 阶段 1: 预解析 UDT 定义
        ParseUdtDefinitions(lines);

        // 阶段 2: 提取 DB 名称和编号
        _parsedDbName = FindDbName(lines);

        // 尝试从 DB 名称中提取编号
        var numMatch = Regex.Match(_parsedDbName, @"DB\s*(\d+)", RegexOptions.IgnoreCase);
        if (numMatch.Success)
        {
            _dbNumber = int.Parse(numMatch.Groups[1].Value);
        }

        // 阶段 3: 解析 DB 成员
        ParseDbMembers(lines);

        return _allTags;
    }

    /// <summary>
    /// 从文件路径解析
    /// </summary>
    public List<PlcTag> ParseFile(string filePath, int dbNumber = 100)
    {
        var content = File.ReadAllText(filePath);
        return Parse(content, dbNumber);
    }

    #region UDT 解析

    private void ParseUdtDefinitions(string[] lines)
    {
        const string propertyRegex = @"\s*\{[^}]*\}\s*";
        string currentUdtName = "";
        bool inUdtStruct = false;
        List<(string Name, string DataType, string Comment)>? currentUdtMembers = null;

        foreach (string line in lines)
        {
            string l = line.Trim();

            if (l.StartsWith("TYPE \""))
            {
                var match = Regex.Match(l, "TYPE \"(.*?)\"");
                if (match.Success)
                {
                    currentUdtName = match.Groups[1].Value;
                    currentUdtMembers = new List<(string, string, string)>();
                    _udtDefinitions[currentUdtName] = currentUdtMembers;
                }
            }
            else if (l.Equals("STRUCT") && !string.IsNullOrEmpty(currentUdtName))
            {
                inUdtStruct = true;
            }
            else if (inUdtStruct && l.Contains(":"))
            {
                // 提取注释
                string comment = "";
                if (l.Contains("//"))
                {
                    var commentIndex = l.IndexOf("//");
                    comment = l.Substring(commentIndex + 2).Trim();
                    l = l.Substring(0, commentIndex).Trim();
                }

                string cleanedLine = Regex.Replace(l, propertyRegex, "").Trim();
                var parts = cleanedLine.Split(new[] { ':' }, 2);
                if (parts.Length < 2) continue;

                string namePart = parts[0].Trim().Replace("\"", "");
                string typePartRaw = parts[1].Trim().TrimEnd(';');

                string typePart = typePartRaw.Contains(":=")
                    ? typePartRaw.Split(new[] { ":=" }, 2, StringSplitOptions.None)[0].Trim()
                    : typePartRaw;

                currentUdtMembers?.Add((namePart, typePart.Replace("\"", "").Trim(), comment));
            }
            else if (l.Equals("END_TYPE"))
            {
                inUdtStruct = false;
                currentUdtName = "";
                currentUdtMembers = null;
            }
        }
    }

    #endregion

    #region DB 解析

    private string FindDbName(string[] lines)
    {
        foreach (string line in lines)
        {
            if (line.Trim().StartsWith("DATA_BLOCK"))
            {
                var match = Regex.Match(line, "DATA_BLOCK \"(.*?)\"");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }
        return "";
    }

    private void ParseDbMembers(string[] lines)
    {
        const string propertyRegex = @"\s*\{[^}]*\}\s*";
        int DbByteOffset = 0;
        int DbBitOffset = 0;
        bool foundDataBlockStart = false;
        bool inBeginSection = false; // BEGIN 段标志：初始值赋值区，非变量声明
        Stack<string> structContext = new();

        foreach (string line in lines)
        {
            string l = line.Trim();

            if (l.StartsWith("DATA_BLOCK", StringComparison.OrdinalIgnoreCase))
            {
                foundDataBlockStart = true;
                continue;
            }

            // BEGIN 段开始 - 之后的内容都是初始值赋值，不是变量
            if (l.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase) && !l.Contains(":"))
            {
                inBeginSection = true;
                continue;
            }

            // END_DATA_BLOCK 结束
            if (l.StartsWith("END_DATA_BLOCK", StringComparison.OrdinalIgnoreCase))
            {
                break; // 整个 DB 解析完成
            }

            // 在 BEGIN 段内，跳过所有行（这些是初始值赋值）
            if (inBeginSection)
            {
                continue;
            }

            // 跳过不需要处理的行
            if (!foundDataBlockStart ||
                l.StartsWith("{") ||
                l.StartsWith("VERSION", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("NON_RETAIN", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("RETAIN", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("TITLE", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("AUTHOR", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("FAMILY", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("NAME", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(l) || l.StartsWith("//"))
            {
                continue;
            }

            if (l.StartsWith("END_STRUCT", StringComparison.OrdinalIgnoreCase))
            {
                if (structContext.Count > 0)
                    structContext.Pop();
                continue;
            }

            // 提取注释
            string comment = "";
            string tempLine = l;
            if (tempLine.Contains("//"))
            {
                var commentIndex = tempLine.IndexOf("//");
                comment = tempLine.Substring(commentIndex + 2).Trim();
                tempLine = tempLine.Substring(0, commentIndex).Trim();
            }

            string cleanedLine = Regex.Replace(tempLine, propertyRegex, "").Trim();

            if (cleanedLine.Contains(":"))
            {
                var parts = cleanedLine.Split(new[] { ':' }, 2);
                if (parts.Length < 2) continue;

                string namePart = parts[0].Trim().Replace("\"", "");
                string typePartRaw = parts[1].Trim().TrimEnd(';');

                string cleanedTypePart = typePartRaw.Contains(":=")
                    ? typePartRaw.Split(new[] { ":=" }, 2, StringSplitOptions.None)[0].Trim()
                    : typePartRaw;

                cleanedTypePart = cleanedTypePart.Replace("\"", "").Trim();

                int currentLevel = structContext.Count;
                string? parentName = structContext.Count > 0 ? structContext.Peek() : null;
                string currentTagFullName = (parentName != null ? parentName + "." : "") + namePart;

                (int size, int alignment) = GetTypeInfo(cleanedTypePart);

                // Array 处理
                if (cleanedTypePart.StartsWith("Array[", StringComparison.OrdinalIgnoreCase))
                {
                    var arrayMatch = Regex.Match(cleanedTypePart, @"Array\[(-?\d+)\.\.(-?\d+)\] of (\S+)", RegexOptions.IgnoreCase);
                    if (!arrayMatch.Success) continue;

                    string elementTypeRaw = arrayMatch.Groups[3].Value.Replace("\"", "").Trim();
                    (int elementSize, int elementAlignment) = _udtDefinitions.ContainsKey(elementTypeRaw)
                        ? CalculateUdtSize(elementTypeRaw)
                        : GetTypeInfo(elementTypeRaw);

                    // 西门子规则：所有 Array 必须从偶数字节开始（最小对齐 2）
                    int arrayAlignment = Math.Max(2, elementAlignment);
                    ApplyPadding(ref DbByteOffset, ref DbBitOffset, arrayAlignment);
                    string containerAddress = $"DB{_dbNumber}.DBX{DbByteOffset}.0";

                    _allTags.Add(new PlcTag
                    {
                        SymbolicName = currentTagFullName,
                        DisplayName = namePart,
                        DisplayDataType = cleanedTypePart,
                        BaseDataType = cleanedTypePart,
                        IsContainer = true,
                        Expander = "[+]",
                        ParentSymbolicName = parentName,
                        IsVisible = currentLevel == 0,
                        Level = currentLevel,
                        Address = containerAddress,
                        Comment = comment
                    });

                    ParseAndAddArrayElements(cleanedTypePart, currentTagFullName, currentLevel + 1, ref DbByteOffset, ref DbBitOffset);
                }
                // UDT 处理
                else if (_udtDefinitions.ContainsKey(cleanedTypePart))
                {
                    (int structSize, int structAlignment) = CalculateUdtSize(cleanedTypePart);

                    ApplyPadding(ref DbByteOffset, ref DbBitOffset, structAlignment);
                    int structBlockStartByte = DbByteOffset;
                    string containerAddress = $"DB{_dbNumber}.DBX{structBlockStartByte}.0";

                    _allTags.Add(new PlcTag
                    {
                        SymbolicName = currentTagFullName,
                        DisplayName = namePart,
                        DisplayDataType = cleanedTypePart,
                        BaseDataType = "UDT",
                        IsContainer = true,
                        Expander = "[+]",
                        ParentSymbolicName = parentName,
                        IsVisible = currentLevel == 0,
                        Level = currentLevel,
                        Address = containerAddress,
                        Comment = comment
                    });

                    ParseAndAddStructMembers(cleanedTypePart, currentTagFullName, currentLevel, ref DbByteOffset, ref DbBitOffset);
                }
                // STRUCT 处理
                else if (cleanedTypePart.Equals("Struct", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPadding(ref DbByteOffset, ref DbBitOffset, alignment);
                    string address = $"DB{_dbNumber}.DBX{DbByteOffset}.0";

                    _allTags.Add(new PlcTag
                    {
                        SymbolicName = currentTagFullName,
                        DisplayName = namePart,
                        DisplayDataType = "STRUCT",
                        BaseDataType = "STRUCT",
                        IsContainer = true,
                        Expander = "[+]",
                        ParentSymbolicName = parentName,
                        IsVisible = currentLevel == 0,
                        Level = currentLevel,
                        Address = address,
                        Comment = comment
                    });
                    structContext.Push(currentTagFullName);
                }
                // 基本数据类型
                else
                {
                    string finalDisplayType = cleanedTypePart;
                    string tagBaseDataType = cleanedTypePart;

                    var strMatch = Regex.Match(cleanedTypePart, @"string\[(\d+)\]", RegexOptions.IgnoreCase);
                    if (strMatch.Success)
                    {
                        finalDisplayType = $"String[{strMatch.Groups[1].Value}]";
                        tagBaseDataType = "String";
                    }

                    // Bool 类型（位寻址）
                    if (size == 0 && cleanedTypePart.Equals("bool", StringComparison.OrdinalIgnoreCase))
                    {
                        string address = $"DB{_dbNumber}.DBX{DbByteOffset}.{DbBitOffset}";
                        _allTags.Add(new PlcTag
                        {
                            SymbolicName = currentTagFullName,
                            DisplayName = namePart,
                            DisplayDataType = finalDisplayType,
                            BaseDataType = tagBaseDataType,
                            IsContainer = false,
                            Expander = "",
                            ParentSymbolicName = parentName,
                            IsVisible = currentLevel == 0,
                            Level = currentLevel,
                            Address = address,
                            Comment = comment
                        });

                        DbBitOffset++;
                        if (DbBitOffset == 8) { DbByteOffset++; DbBitOffset = 0; }
                    }
                    else
                    {
                        ApplyPadding(ref DbByteOffset, ref DbBitOffset, alignment);

                        string formattedAddress = size switch
                        {
                            1 => $"DB{_dbNumber}.DBB{DbByteOffset}",
                            2 when DbByteOffset % 2 == 0 => $"DB{_dbNumber}.DBW{DbByteOffset}",
                            4 when DbByteOffset % 2 == 0 => $"DB{_dbNumber}.DBD{DbByteOffset}",
                            8 when DbByteOffset % 2 == 0 => $"DB{_dbNumber}.DBL{DbByteOffset}",
                            _ => $"DB{_dbNumber}.DBB{DbByteOffset}"
                        };

                        _allTags.Add(new PlcTag
                        {
                            SymbolicName = currentTagFullName,
                            DisplayName = namePart,
                            DisplayDataType = finalDisplayType,
                            BaseDataType = tagBaseDataType,
                            IsContainer = false,
                            Expander = "",
                            ParentSymbolicName = parentName,
                            IsVisible = currentLevel == 0,
                            Level = currentLevel,
                            Address = formattedAddress,
                            Comment = comment
                        });

                        DbByteOffset += size;
                        DbBitOffset = 0;
                    }
                }
            }
        }

        // 清理无效标签
        _allTags.RemoveAll(t =>
            t.DisplayName.StartsWith("TYPE", StringComparison.OrdinalIgnoreCase) ||
            t.DisplayName.StartsWith("VERSION", StringComparison.OrdinalIgnoreCase) ||
            t.DisplayName.StartsWith("STRUCT", StringComparison.OrdinalIgnoreCase) ||
            t.DisplayName.StartsWith("END_STRUCT", StringComparison.OrdinalIgnoreCase) ||
            t.DisplayName.StartsWith("END_TYPE", StringComparison.OrdinalIgnoreCase)
        );
    }

    #endregion

    #region 类型处理

    /// <summary>
    /// 获取数据类型的大小和对齐要求
    /// </summary>
    private (int Size, int Alignment) GetTypeInfo(string dataType)
    {
        dataType = dataType.Trim().ToLower();

        // String[N] 处理
        var strMatch = Regex.Match(dataType, @"string\[(\d+)\]");
        if (strMatch.Success)
        {
            int length = int.Parse(strMatch.Groups[1].Value);
            return (2 + length, 2);  // 2 字节头 + 字符长度
        }

        // WString[N] 处理
        var wstrMatch = Regex.Match(dataType, @"wstring\[(\d+)\]");
        if (wstrMatch.Success)
        {
            int length = int.Parse(wstrMatch.Groups[1].Value);
            return (4 + length * 2, 2);  // 4 字节头 + 字符长度 * 2
        }

        return dataType switch
        {
            "bool" => (0, 1),  // Bool 特殊：位寻址
            "byte" or "sint" or "usint" or "char" => (1, 1),
            "int" or "word" or "uint" or "s5time" => (2, 2),
            "dint" or "dword" or "real" or "udint" => (4, 2),
            "lint" or "lword" or "lreal" or "ulint" or "ltime" => (8, 2),
            "time" => (4, 2),           // TIME = 4 字节 (毫秒)
            "date" => (2, 2),           // DATE = 2 字节 (天数)
            "time_of_day" or "tod" => (4, 2),  // TIME_OF_DAY = 4 字节
            "date_and_time" or "dt" => (8, 2), // DATE_AND_TIME = 8 字节
            "dtl" => (12, 2),           // DTL = 12 字节
            "struct" => (0, 2),
            _ => (1, 1)  // 未知类型默认 1 字节
        };
    }

    /// <summary>
    /// 计算 UDT 的总大小和最大对齐
    /// </summary>
    private (int TotalSize, int MaxAlignment) CalculateUdtSize(string udtName)
    {
        if (!_udtDefinitions.TryGetValue(udtName, out var members))
        {
            return (1, 1);
        }

        int currentByteOffset = 0;
        int currentBitOffset = 0;
        int maxAlignment = 1;

        foreach (var member in members)
        {
            string memberType = member.DataType.Replace("\"", "").Trim();

            (int memberSize, int memberAlignment) = GetTypeInfo(memberType);

            if (_udtDefinitions.ContainsKey(memberType))
            {
                (memberSize, memberAlignment) = CalculateUdtSize(memberType);
            }
            else if (memberType.StartsWith("Array[", StringComparison.OrdinalIgnoreCase))
            {
                var arrayMatch = Regex.Match(memberType, @"Array\[(-?\d+)\.\.(-?\d+)\] of (\S+)", RegexOptions.IgnoreCase);
                if (!arrayMatch.Success) continue;

                int lowerBound = int.Parse(arrayMatch.Groups[1].Value);
                int upperBound = int.Parse(arrayMatch.Groups[2].Value);
                string elementType = arrayMatch.Groups[3].Value.Replace("\"", "").Trim();
                int totalElements = upperBound - lowerBound + 1;

                (int elementSize, int elementAlignment) = _udtDefinitions.ContainsKey(elementType)
                    ? CalculateUdtSize(elementType)
                    : GetTypeInfo(elementType);

                memberAlignment = elementAlignment;
                if (elementType.Equals("bool", StringComparison.OrdinalIgnoreCase))
                {
                    memberSize = (totalElements + 7) / 8;
                }
                else
                {
                    memberSize = totalElements * elementSize;
                }
            }

            maxAlignment = Math.Max(maxAlignment, memberAlignment);
            ApplyPadding(ref currentByteOffset, ref currentBitOffset, memberAlignment);

            if (memberType.Equals("bool", StringComparison.OrdinalIgnoreCase))
            {
                currentBitOffset++;
                if (currentBitOffset == 8)
                {
                    currentByteOffset++;
                    currentBitOffset = 0;
                }
            }
            else
            {
                if (currentBitOffset > 0)
                {
                    currentByteOffset++;
                    currentBitOffset = 0;
                }
                currentByteOffset += memberSize;
            }
        }

        if (currentBitOffset > 0) currentByteOffset++;

        int totalSize = currentByteOffset;
        if (totalSize % maxAlignment != 0)
        {
            totalSize += maxAlignment - (totalSize % maxAlignment);
        }

        return (totalSize, maxAlignment);
    }

    /// <summary>
    /// 应用对齐填充
    /// </summary>
    private void ApplyPadding(ref int byteOffset, ref int bitOffset, int alignment)
    {
        if (bitOffset > 0)
        {
            byteOffset++;
            bitOffset = 0;
        }

        if (alignment > 1 && byteOffset % alignment != 0)
        {
            byteOffset += alignment - (byteOffset % alignment);
        }
    }

    #endregion

    #region 递归解析

    private void ParseAndAddStructMembers(string udtName, string parentTagFullName, int parentLevel,
        ref int currentByteOffset, ref int currentBitOffset)
    {
        if (!_udtDefinitions.TryGetValue(udtName, out var members))
            return;

        int udtStartOffset = currentByteOffset;
        (int udtTotalSize, int udtAlignment) = CalculateUdtSize(udtName);

        ApplyPadding(ref currentByteOffset, ref currentBitOffset, udtAlignment);
        udtStartOffset = currentByteOffset;

        int structInternalByteOffset = udtStartOffset;
        int structInternalBitOffset = 0;
        int nextLevel = parentLevel + 1;

        foreach (var member in members)
        {
            string memberName = member.Name;
            string memberType = member.DataType.Replace("\"", "").Trim();
            string memberSymbolicName = parentTagFullName + "." + memberName;
            string memberComment = member.Comment;

            (int memberSize, int memberAlignment) = GetTypeInfo(memberType);

            bool isMemberArray = memberType.StartsWith("Array[", StringComparison.OrdinalIgnoreCase);
            bool isMemberUdt = _udtDefinitions.ContainsKey(memberType);

            string memberBaseType = memberType;
            string memberDisplayType = memberType;

            var strMatch_Member = Regex.Match(memberType, @"string\[(\d+)\]", RegexOptions.IgnoreCase);
            if (strMatch_Member.Success)
            {
                memberBaseType = "String";
                memberDisplayType = $"String[{strMatch_Member.Groups[1].Value}]";
            }

            if (isMemberUdt)
            {
                (memberSize, memberAlignment) = CalculateUdtSize(memberType);
                memberBaseType = "UDT";
            }
            else if (isMemberArray)
            {
                var match = Regex.Match(memberType, @"Array\[(-?\d+)\.\.(-?\d+)\] of (\S+)");
                string elementType = match.Groups[3].Value.Replace("\"", "").Trim();
                (int elementSize, int elementAlignment) = _udtDefinitions.ContainsKey(elementType)
                    ? CalculateUdtSize(elementType)
                    : GetTypeInfo(elementType);

                // 所有 Array 最小对齐 2
                memberAlignment = Math.Max(2, elementAlignment);
            }

            ApplyPadding(ref structInternalByteOffset, ref structInternalBitOffset, memberAlignment);

            string memberAddress = (memberSize, structInternalByteOffset % 2, structInternalBitOffset) switch
            {
                (1, _, 0) => $"DB{_dbNumber}.DBB{structInternalByteOffset}",
                (2, 0, 0) => $"DB{_dbNumber}.DBW{structInternalByteOffset}",
                (4, 0, 0) => $"DB{_dbNumber}.DBD{structInternalByteOffset}",
                (8, 0, 0) => $"DB{_dbNumber}.DBL{structInternalByteOffset}",
                (0, _, _) => $"DB{_dbNumber}.DBX{structInternalByteOffset}.{structInternalBitOffset}",
                (_, _, 0) => $"DB{_dbNumber}.DBB{structInternalByteOffset}",
                _ => $"DB{_dbNumber}.DBX{structInternalByteOffset}.{structInternalBitOffset}"
            };

            _allTags.Add(new PlcTag
            {
                SymbolicName = memberSymbolicName,
                DisplayName = memberName,
                DisplayDataType = memberDisplayType,
                BaseDataType = memberBaseType,
                IsContainer = isMemberArray || isMemberUdt,
                Expander = (isMemberArray || isMemberUdt) ? "[+]" : "",
                ParentSymbolicName = parentTagFullName,
                IsVisible = false,
                Level = nextLevel,
                Address = memberAddress,
                Comment = memberComment
            });

            if (isMemberUdt)
            {
                ParseAndAddStructMembers(memberType, memberSymbolicName, nextLevel, ref structInternalByteOffset, ref structInternalBitOffset);
            }
            else if (isMemberArray)
            {
                ParseAndAddArrayElements(memberType, memberSymbolicName, nextLevel, ref structInternalByteOffset, ref structInternalBitOffset);
            }
            else
            {
                if (memberSize == 0 && memberType.Equals("bool", StringComparison.OrdinalIgnoreCase))
                {
                    structInternalBitOffset++;
                    if (structInternalBitOffset == 8) { structInternalByteOffset++; structInternalBitOffset = 0; }
                }
                else
                {
                    if (structInternalBitOffset > 0)
                    {
                        structInternalByteOffset++;
                        structInternalBitOffset = 0;
                    }
                    structInternalByteOffset += memberSize;
                    structInternalBitOffset = 0;
                }
            }
        }

        currentByteOffset = udtStartOffset + udtTotalSize;
        currentBitOffset = 0;
    }

    private void ParseAndAddArrayElements(string arrayType, string parentTagFullName, int parentLevel,
        ref int currentByteOffset, ref int currentBitOffset)
    {
        var arrayMatch = Regex.Match(arrayType, @"Array\[(-?\d+)\.\.(-?\d+)\] of (\S+)", RegexOptions.IgnoreCase);
        if (!arrayMatch.Success) return;

        int lowerBound = int.Parse(arrayMatch.Groups[1].Value);
        int upperBound = int.Parse(arrayMatch.Groups[2].Value);
        string elementTypeRaw = arrayMatch.Groups[3].Value.Replace("\"", "").Trim();

        (int elementSize, int elementAlignment) = _udtDefinitions.ContainsKey(elementTypeRaw)
            ? CalculateUdtSize(elementTypeRaw)
            : GetTypeInfo(elementTypeRaw);

        bool isBoolArray = elementTypeRaw.Equals("bool", StringComparison.OrdinalIgnoreCase);
        bool isContainerArray = _udtDefinitions.ContainsKey(elementTypeRaw) ||
                                elementTypeRaw.Equals("struct", StringComparison.OrdinalIgnoreCase);

        for (int i = lowerBound; i <= upperBound; i++)
        {
            string elementSymbolicName = $"{parentTagFullName}[{i}]";
            string elementName = $"[{i}]";
            string elementDisplayType = elementTypeRaw;
            string elementBaseType = elementTypeRaw;

            if (isContainerArray)
            {
                ApplyPadding(ref currentByteOffset, ref currentBitOffset, elementAlignment);
                string elementAddress = $"DB{_dbNumber}.DBX{currentByteOffset}.0";
                elementBaseType = _udtDefinitions.ContainsKey(elementTypeRaw) ? "UDT" : "STRUCT";

                _allTags.Add(new PlcTag
                {
                    SymbolicName = elementSymbolicName,
                    DisplayName = elementName,
                    DisplayDataType = elementTypeRaw,
                    BaseDataType = elementBaseType,
                    IsContainer = true,
                    Expander = "[+]",
                    ParentSymbolicName = parentTagFullName,
                    IsVisible = false,
                    Level = parentLevel,
                    Address = elementAddress
                });

                ParseAndAddStructMembers(elementTypeRaw, elementSymbolicName, parentLevel, ref currentByteOffset, ref currentBitOffset);
            }
            else if (isBoolArray)
            {
                string elementAddress = $"DB{_dbNumber}.DBX{currentByteOffset}.{currentBitOffset}";

                _allTags.Add(new PlcTag
                {
                    SymbolicName = elementSymbolicName,
                    DisplayName = elementName,
                    DisplayDataType = elementTypeRaw,
                    BaseDataType = elementTypeRaw,
                    IsContainer = false,
                    Expander = "",
                    ParentSymbolicName = parentTagFullName,
                    IsVisible = false,
                    Level = parentLevel,
                    Address = elementAddress
                });

                currentBitOffset++;
                if (currentBitOffset == 8) { currentByteOffset++; currentBitOffset = 0; }
            }
            else
            {
                var strMatch = Regex.Match(elementTypeRaw, @"string\[(\d+)\]", RegexOptions.IgnoreCase);
                if (strMatch.Success)
                {
                    elementDisplayType = $"String[{strMatch.Groups[1].Value}]";
                    elementBaseType = "String";
                }

                string elementAddress = elementSize switch
                {
                    1 => $"DB{_dbNumber}.DBB{currentByteOffset}",
                    2 when currentByteOffset % 2 == 0 => $"DB{_dbNumber}.DBW{currentByteOffset}",
                    4 when currentByteOffset % 2 == 0 => $"DB{_dbNumber}.DBD{currentByteOffset}",
                    8 when currentByteOffset % 2 == 0 => $"DB{_dbNumber}.DBL{currentByteOffset}",
                    _ => $"DB{_dbNumber}.DBB{currentByteOffset}"
                };

                _allTags.Add(new PlcTag
                {
                    SymbolicName = elementSymbolicName,
                    DisplayName = elementName,
                    DisplayDataType = elementDisplayType,
                    BaseDataType = elementBaseType,
                    IsContainer = false,
                    Expander = "",
                    ParentSymbolicName = parentTagFullName,
                    IsVisible = false,
                    Level = parentLevel,
                    Address = elementAddress
                });

                currentByteOffset += elementSize;
            }
        }

        if (currentBitOffset > 0)
        {
            currentByteOffset++;
            currentBitOffset = 0;
        }
        ApplyPadding(ref currentByteOffset, ref currentBitOffset, 2);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 获取指定标签的读取字节数
    /// </summary>
    public int GetReadByteCount(PlcTag tag)
    {
        string dataTypeToCheck = tag.DisplayDataType.Trim().ToLower();

        // Array 处理
        var arrayMatch = Regex.Match(dataTypeToCheck, @"array\[(-?\d+)\.\.(-?\d+)\] of (\S+)");
        if (arrayMatch.Success)
        {
            int lowerBound = int.Parse(arrayMatch.Groups[1].Value);
            int upperBound = int.Parse(arrayMatch.Groups[2].Value);
            string elementType = arrayMatch.Groups[3].Value.Replace("\"", "").Trim().ToLower();
            int totalElements = upperBound - lowerBound + 1;

            (int elementSize, _) = _udtDefinitions.ContainsKey(elementType)
                ? CalculateUdtSize(elementType)
                : GetTypeInfo(elementType);

            if (elementType.Equals("bool"))
            {
                return (totalElements + 7) / 8;
            }
            return totalElements * elementSize;
        }

        // String 处理
        var strMatch = Regex.Match(dataTypeToCheck, @"string\[(\d+)\]");
        if (strMatch.Success)
        {
            int length = int.Parse(strMatch.Groups[1].Value);
            return 2 + length;
        }

        (int size, _) = GetTypeInfo(tag.BaseDataType.Trim().ToLower());
        if (size == 0 && tag.BaseDataType.Equals("bool", StringComparison.OrdinalIgnoreCase))
            return 1;

        return size;
    }

    /// <summary>
    /// 获取数据类型的大小（公开方法，用于兼容现有代码）
    /// </summary>
    public int GetPublicTypeSize(string dataType)
    {
        (int size, _) = GetTypeInfo(dataType);
        return size;
    }

    /// <summary>
    /// 获取 UDT 定义（公开方法，用于兼容现有代码）
    /// </summary>
    public UdtDefinition? GetUdtDefinition(string udtName)
    {
        if (_udtDefinitions.TryGetValue(udtName, out var members))
        {
            return new UdtDefinition
            {
                Name = udtName,
                Definition = string.Join("\n", members.Select(m => $"{m.Name} : {m.DataType}; // {m.Comment}"))
            };
        }
        return null;
    }

    #endregion
}

