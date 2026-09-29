using System.Globalization;

namespace GalNet.Core.Variable;

/// <summary>
/// 变量值 —— Discriminated Union，每个子类只持有自己的值，无运行时类型转换开销。
/// </summary>
public abstract class VariableValue
{
    private VariableValue() { } // 封闭继承，只允许内部子类

    // ── 子类型 ──────────────────────────────────────────────────────────────

    public sealed class Bool : VariableValue
    {
        public bool Value { get; }
        public Bool(bool value) { Value = value; }
        public override VariableType Type => VariableType.Bool;
        public override bool AsBool() => Value;
        public override int AsInt() => Value ? 1 : 0;
        public override float AsFloat() => Value ? 1f : 0f;
        public override string AsString() => Value ? "true" : "false";
        public override string Serialize() => AsString();
        public override string ToString() => AsString();
    }

    public sealed class Int : VariableValue
    {
        public int Value { get; }
        public Int(int value) { Value = value; }
        public override VariableType Type => VariableType.Int;
        public override bool AsBool() => Value != 0;
        public override int AsInt() => Value;
        public override float AsFloat() => Value;
        public override string AsString() => Value.ToString();
        public override string Serialize() => Value.ToString();
        public override string ToString() => Value.ToString();
    }

    public sealed class Float : VariableValue
    {
        public float Value { get; }
        public Float(float value) { Value = value; }
        public override VariableType Type => VariableType.Float;
        public override bool AsBool() => Value != 0f;
        public override int AsInt() => (int)Value;
        public override float AsFloat() => Value;
        public override string AsString() => Value.ToString(CultureInfo.InvariantCulture);
        public override string Serialize() => Value.ToString("R", CultureInfo.InvariantCulture);
        public override string ToString() => AsString();
    }

    public sealed class String : VariableValue
    {
        public string Value { get; }
        public String(string value) { Value = value; }
        public override VariableType Type => VariableType.String;
        public override bool AsBool() => !string.IsNullOrEmpty(Value);
        public override int AsInt() => int.TryParse(Value, out var i) ? i : 0;
        public override float AsFloat() => float.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
        public override string AsString() => Value;
        public override string Serialize() => Value;
        public override string ToString() => Value;
    }

    // ── 抽象成员 ────────────────────────────────────────────────────────────

    public abstract VariableType Type { get; }

    public abstract bool AsBool();
    public abstract int AsInt();
    public abstract float AsFloat();
    public abstract string AsString();

    /// <summary>序列化为字符串（用于存档）。</summary>
    public abstract string Serialize();

    // ── 工厂方法 ────────────────────────────────────────────────────────────

    public static VariableValue From(bool v) => new Bool(v);
    public static VariableValue From(int v) => new Int(v);
    public static VariableValue From(float v) => new Float(v);
    public static VariableValue From(string v) => new String(v);

    /// <summary>从 object 推断类型并包装。</summary>
    public static VariableValue FromObject(object value) => value switch
    {
        bool b => new Bool(b),
        int i => new Int(i),
        float f => new Float(f),
        double d => new Float((float)d),
        long l => new Int((int)l),
        string s => new String(s),
        _ => new String(value.ToString() ?? "")
    };

    /// <summary>从序列化字符串 + 类型还原（读档用）。</summary>
    public static VariableValue Deserialize(string raw, VariableType type) => type switch
    {
        VariableType.Bool => new Bool(bool.TryParse(raw, out var b) && b),
        VariableType.Int => new Int(int.TryParse(raw, out var i) ? i : 0),
        VariableType.Float => new Float(float.TryParse(raw, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var f) ? f : 0f),
        _ => new String(raw)
    };
}
