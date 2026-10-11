namespace GalNet.Core.I18n;

/// <summary>
/// I18n 键 —— 包装树形路径键，非 string，分配后不可变。
/// 运行时由宿主提供的轻量文本解析器解析为目标语言文本。
/// 键基于稳定 ID（组/条目 ID），不受重命名影响。
/// </summary>
public sealed class I18nKey
{
    /// <summary>点分隔的路径键（如 "group_abc.entry_3.content"）</summary>
    public string Key { get; }

    public I18nKey(string key)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
    }

    public override string ToString() => Key;

    public override bool Equals(object? obj) =>
        obj is I18nKey other && Key == other.Key;

    public override int GetHashCode() => Key.GetHashCode();

    public static bool operator ==(I18nKey? a, I18nKey? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.Key == b.Key);

    public static bool operator !=(I18nKey? a, I18nKey? b) => !(a == b);

    // ── 工厂方法 ──

    /// <summary>Creates a key using the raw authoring-entry ID at generation time.</summary>
    /// <remarks>
    /// Editor tooling generates a key once and stores it in the content parameter.
    /// Compilation and execution pass that key unchanged, even when composite
    /// expansion changes entry positions.
    /// </remarks>
    public static I18nKey ForEntry(string groupId, string sourceEntryId, string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEntryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new($"group.{groupId}.entry.{sourceEntryId}.{field}");
    }

    /// <summary>Creates a key from the raw entry's numeric position when the editor generates it.</summary>
    public static I18nKey ForEntry(string groupId, int sourceEntryId, string field) =>
        ForEntry(groupId, sourceEntryId.ToString(System.Globalization.CultureInfo.InvariantCulture), field);

    /// <summary>为分支的某个字段生成键</summary>
    public static I18nKey ForBranchOption(string branchId, int optionIndex, string field) =>
        new($"{branchId}.option_{optionIndex}.{field}");
}
