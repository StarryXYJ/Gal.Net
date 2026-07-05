namespace GalNet.Core.Serialization;

/// <summary>
/// .galgroup 格式解析器 —— 将文本行解析为参数字典，支持反斜杠转义。
///
/// 格式：条目类型 : 参数1:值1; 参数2:值2; ...
/// 第一段（首个未转义的 : 之前）为条目类型，剩余部分按 ;  分割为参数，
/// 每个参数按 :  分割为键值对。
///
/// 转义规则：
///   \\ → 字面量 \
///   \: → 字面量 :
///   \; → 字面量 ;
///   \<其他> → 字面量 \<其他>（反斜杠保留，未知转义不强拆）
/// </summary>
public static class GalgroupParser
{
    /// <summary>
    /// 解析 .galgroup 文本为条目数据列表（不含类型实例化，仅提取参数字典）。
    /// 返回 (lineNumber, entryType, params) 元组列表。
    /// </summary>
    public static List<(int LineNumber, string EntryType, Dictionary<string, string> Params)> Parse(string content)
    {
        var result = new List<(int, string, Dictionary<string, string>)>();
        var lines = content.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                continue;

            var (entryType, parameters) = ParseLine(line);
            result.Add((i + 1, entryType, parameters));
        }

        return result;
    }

    /// <summary>将参数字典序列化回 .galgroup 单行格式。</summary>
    public static string Serialize(string entryType, Dictionary<string, string> parameters)
    {
        if (parameters.Count == 0)
            return entryType;

        var parts = new List<string>();
        foreach (var (key, value) in parameters)
        {
            parts.Add(string.IsNullOrEmpty(value)
                ? Escape(key)
                : $"{Escape(key)}: {Escape(value)}");
        }

        return $"{entryType} : {string.Join("; ", parts)}";
    }

    /// <summary>转义字符串：将 \ : ; 前插入反斜杠保护。</summary>
    public static string Escape(string raw)
    {
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            sb.Append(ch switch
            {
                '\\' => @"\\",
                ':' => @"\:",
                ';' => @"\;",
                _ => ch
            });
        }
        return sb.ToString();
    }

    /// <summary>反转义字符串：将转义序列还原。仅处理已知转义（\\ \: \;），未知转义保留反斜杠。</summary>
    public static string Unescape(string escaped)
    {
        var sb = new System.Text.StringBuilder(escaped.Length);
        for (var i = 0; i < escaped.Length; i++)
        {
            if (escaped[i] == '\\' && i + 1 < escaped.Length)
            {
                var next = escaped[i + 1];
                if (next == '\\' || next == ':' || next == ';')
                {
                    // 已知转义：\\ → \ , \: → : , \; → ;
                    sb.Append(next);
                }
                else
                {
                    // 未知转义：保留反斜杠
                    sb.Append('\\');
                    sb.Append(next);
                }
                i++;
            }
            else
            {
                sb.Append(escaped[i]);
            }
        }
        return sb.ToString();
    }

    // ── 内部解析 ──

    private static (string EntryType, Dictionary<string, string> Params) ParseLine(string line)
    {
        var paramsDict = new Dictionary<string, string>();

        // 找第一个未转义的 : 分隔条目类型与参数
        var colonIndex = IndexOfUnescaped(line, ':');
        if (colonIndex < 0)
            return (line.Trim(), paramsDict);

        var entryType = line[..colonIndex].Trim();
        var paramsPart = line[(colonIndex + 1)..].Trim();

        if (string.IsNullOrEmpty(paramsPart))
            return (entryType, paramsDict);

        // 按未转义的 "; " 分割参数段
        var segments = SplitUnescaped(paramsPart, "; ");
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            var kvIndex = IndexOfUnescaped(segment, ':');
            if (kvIndex < 0)
            {
                // 无值参数（bool flag）
                paramsDict[Unescape(segment.Trim())] = "";
            }
            else
            {
                var key = Unescape(segment[..kvIndex].Trim());
                var value = Unescape(segment[(kvIndex + 1)..].Trim());
                paramsDict[key] = value;
            }
        }

        return (entryType, paramsDict);
    }

    /// <summary>
    /// 查找第一个未转义的目标字符索引。\ 前缀跳过下一个字符。
    /// </summary>
    private static int IndexOfUnescaped(string text, char target)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i++; // 跳过转义字符
                continue;
            }
            if (text[i] == target)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// 按未转义的分隔符字符串分割。分隔符中的 \ 和特殊字符均需考虑转义。
    /// </summary>
    private static List<string> SplitUnescaped(string text, string delimiter)
    {
        var result = new List<string>();
        var start = 0;

        for (var i = 0; i <= text.Length - delimiter.Length; i++)
        {
            // 跳过转义字符
            if (text[i] == '\\')
            {
                i++; // 跳过被转义的字符
                continue;
            }

            // 检查是否匹配分隔符
            var match = true;
            for (var d = 0; d < delimiter.Length; d++)
            {
                if (text[i + d] != delimiter[d])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                result.Add(text[start..i]);
                i += delimiter.Length - 1; // 跳过整个分隔符
                start = i + 1;
            }
        }

        result.Add(text[start..]);
        return result;
    }
}
