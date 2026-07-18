using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Serialization;

namespace GalNet.Runtime.Loader;

/// <summary>
/// 加载 .galgroup 文件，通过 Core EntryRegistry 创建具体 Entry 并注入 Group。
/// 条目类型名直接使用英文标识（text / audio / layer / ...），
/// 编辑器中通过 i18n 键（如 editor.handler.text）查找显示名称。
/// </summary>
public static class GalgroupLoader
{
    /// <summary>从文件加载并填入目标 Group。</summary>
    public static void LoadIntoGroup(Group group, string galgroupPath)
    {
        var content = File.ReadAllText(galgroupPath);
        LoadIntoGroupFromContent(group, content);
    }

    /// <summary>从文本内容解析并填入目标 Group（用于测试 / 内存模式）。</summary>
    public static void LoadIntoGroupFromContent(Group group, string content)
    {
        var parsed = GalgroupParser.Parse(content);
        var entries = new List<Entry>();

        foreach (var (_, entryType, parameters) in parsed)
        {
            var condition = parameters.Remove("condition", out var cond) ? cond : "";
            parameters.Remove("__editorId");
            var entry = EntryRegistry.Create(entryType, entries.Count + 1, condition, parameters);
            entries.Add(entry);
        }

        group.Entries.Clear();
        group.Entries.AddRange(entries);
    }
}
