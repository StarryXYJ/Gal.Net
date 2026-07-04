namespace GalNet.Core.Variable;

/// <summary>
/// 变量封装对象。player 和 save 变量共用命名空间，UID 不可重名。
/// </summary>
public sealed class Variable
{
    /// <summary>全局唯一标识</summary>
    public string Uid { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>变量名（显示用）</summary>
    public string Name { get; set; } = "";

    /// <summary>值类型</summary>
    public VariableType Type { get; set; } = VariableType.String;

    /// <summary>原始字符串值（存取时按 Type 转换）</summary>
    public string Value { get; set; } = "";

    public bool AsBool() => bool.TryParse(Value, out var r) && r;
    public int AsInt() => int.TryParse(Value, out var r) ? r : 0;
    public float AsFloat() => float.TryParse(Value, out var r) ? r : 0f;
    public string AsString() => Value;

    public void SetValue(bool v) => Value = v.ToString().ToLowerInvariant();
    public void SetValue(int v) => Value = v.ToString();
    public void SetValue(float v) => Value = v.ToString();
    public void SetValue(string v) => Value = v;
}
