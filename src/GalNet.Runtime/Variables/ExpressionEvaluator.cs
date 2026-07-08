using GalNet.Core.Variable;

namespace GalNet.Runtime.Variables;

/// <summary>
/// 条件/表达式求值器。
/// 变量通过占位符 [name] 引用，运行时替换为字面值后求值。
/// 支持的运算：比较 (== != < > <= >=)、逻辑 (&& ||)、算术 (+ - * /)。
/// </summary>
public sealed class ExpressionEvaluator
{
    private readonly VariableStore _store;

    public ExpressionEvaluator(VariableStore store)
    {
        _store = store;
    }

    /// <summary>
    /// 求值条件表达式，返回 bool。空表达式 = true。
    /// </summary>
    public bool EvaluateCondition(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return true;

        try
        {
            var result = Evaluate(expression);
            return result switch
            {
                bool b => b,
                int i => i != 0,
                float f => f != 0f,
                string s => !string.IsNullOrEmpty(s),
                null => false,
                _ => true
            };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 求值通用表达式。先替换 [var]，再解析求值。
    /// </summary>
    public object? Evaluate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return null;

        var resolved = ResolveVariables(expression);
        return EvaluateSimple(resolved);
    }

    // ── 变量替换 ──

    private string ResolveVariables(string expr)
    {
        var sb = new System.Text.StringBuilder();
        var i = 0;
        while (i < expr.Length)
        {
            if (expr[i] == '[')
            {
                var close = expr.IndexOf(']', i);
                if (close < 0) { sb.Append(expr[i]); i++; }
                else
                {
                    var varPath = expr[(i + 1)..close];
                    var value = _store.TryGet(varPath, out var v)
                        ? FormatLiteral(v)
                        : "null";
                    sb.Append(value);
                    i = close + 1;
                }
            }
            else { sb.Append(expr[i]); i++; }
        }
        return sb.ToString();
    }

    private static string FormatLiteral(Variable v) => v.Type switch
    {
        VariableType.Bool => v.AsBool() ? "true" : "false",
        VariableType.Int => v.AsInt().ToString(),
        VariableType.Float => v.AsFloat().ToString(System.Globalization.CultureInfo.InvariantCulture),
        VariableType.String => $"\"{v.AsString().Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        _ => $"\"{v.AsString()}\""
    };

    // ── 简化求值：逻辑 > 比较 > 加减 > 乘除 > 基础值 ──

    private static object? EvaluateSimple(string expr)
    {
        expr = expr.Trim();

        // 逻辑运算 (最低优先级)
        var orIdx = FindTopLevelOp(expr, "||");
        if (orIdx >= 0)
        {
            var left = EvaluateSimple(expr[..orIdx]);
            var right = EvaluateSimple(expr[(orIdx + 2)..]);
            return IsTruthy(left) || IsTruthy(right);
        }

        var andIdx = FindTopLevelOp(expr, "&&");
        if (andIdx >= 0)
        {
            var left = EvaluateSimple(expr[..andIdx]);
            var right = EvaluateSimple(expr[(andIdx + 2)..]);
            return IsTruthy(left) && IsTruthy(right);
        }

        // 比较运算
        var compIdx = FindTopLevelOp(expr, "==");
        if (compIdx >= 0) return Compare(expr[..compIdx], expr[(compIdx + 2)..]) == 0;

        compIdx = FindTopLevelOp(expr, "!=");
        if (compIdx >= 0) return Compare(expr[..compIdx], expr[(compIdx + 2)..]) != 0;

        compIdx = FindTopLevelOp(expr, ">=");
        if (compIdx >= 0) return Compare(expr[..compIdx], expr[(compIdx + 2)..]) >= 0;

        compIdx = FindTopLevelOp(expr, "<=");
        if (compIdx >= 0) return Compare(expr[..compIdx], expr[(compIdx + 2)..]) <= 0;

        compIdx = FindTopLevelOp(expr, ">");
        if (compIdx >= 0) return Compare(expr[..compIdx], expr[(compIdx + 1)..]) > 0;

        compIdx = FindTopLevelOp(expr, "<");
        if (compIdx >= 0) return Compare(expr[..compIdx], expr[(compIdx + 1)..]) < 0;

        // 加减法（从右向左扫描以满足左结合）
        var addIdx = FindTopLevelOpRightToLeft(expr, "+");
        if (addIdx >= 0)
        {
            var left = EvaluateSimple(expr[..addIdx]);
            var right = EvaluateSimple(expr[(addIdx + 1)..]);
            return ArithOp(left, right, '+');
        }

        var subIdx = FindTopLevelOpRightToLeft(expr, "-");
        if (subIdx > 0 && IsInfixMinus(expr, subIdx))
        {
            var left = EvaluateSimple(expr[..subIdx]);
            var right = EvaluateSimple(expr[(subIdx + 1)..]);
            return ArithOp(left, right, '-');
        }

        // 乘除法（同样左结合）
        var mulIdx = FindTopLevelOpRightToLeft(expr, "*");
        if (mulIdx >= 0)
        {
            var left = EvaluateSimple(expr[..mulIdx]);
            var right = EvaluateSimple(expr[(mulIdx + 1)..]);
            return ArithOp(left, right, '*');
        }

        var divIdx = FindTopLevelOpRightToLeft(expr, "/");
        if (divIdx >= 0)
        {
            var left = EvaluateSimple(expr[..divIdx]);
            var right = EvaluateSimple(expr[(divIdx + 1)..]);
            return ArithOp(left, right, '/');
        }

        // 负号前缀
        if (expr.StartsWith('-'))
            return Negate(EvaluateSimple(expr[1..]));

        // 括号
        if (expr.StartsWith('(') && expr.EndsWith(')'))
            return EvaluateSimple(expr[1..^1]);

        // 字面量
        return ParseLiteral(expr);
    }

    /// <summary>
    /// 从左到右查找不在括号内的第一个顶层操作符位置。
    /// </summary>
    private static int FindTopLevelOp(string expr, string op)
    {
        var depth = 0;
        for (var i = 0; i < expr.Length - op.Length + 1; i++)
        {
            if (expr[i] == '(') depth++;
            else if (expr[i] == ')') depth--;
            else if (depth == 0 && expr[i..].StartsWith(op))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// 从右到左查找不在括号内的最后一个顶层操作符位置（确保左结合性，如 1-2-3 解析为 (1-2)-3）。
    /// </summary>
    private static int FindTopLevelOpRightToLeft(string expr, string op)
    {
        var depth = 0;
        for (var i = expr.Length - op.Length; i >= 0; i--)
        {
            if (expr[i] == ')') depth++;
            else if (expr[i] == '(') depth--;
            else if (depth == 0 && expr[i..].StartsWith(op))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// 判断指定的 '-' 字符是否是二元中缀减法（而非单元前缀负号）。
    /// </summary>
    private static bool IsInfixMinus(string expr, int idx)
    {
        for (var i = idx - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(expr[i])) continue;
            var c = expr[i];
            return char.IsLetterOrDigit(c) || c == ')' || c == '"';
        }
        return false;
    }

    // ── 字面量解析 ──

    private static object? ParseLiteral(string s)
    {
        s = s.Trim();
        if (s == "true") return true;
        if (s == "false") return false;
        if (s == "null") return null;

        // 字符串
        if (s.StartsWith('"') && s.EndsWith('"'))
            return s[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");

        // 浮点数
        if (s.Contains('.'))
            return float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        // 整数
        if (int.TryParse(s, out var i)) return i;

        // 无法解析，返回原始字符串
        return s;
    }

    // ── 辅助 ──

    private static int Compare(string a, string b)
    {
        var va = EvaluateSimple(a.Trim());
        var vb = EvaluateSimple(b.Trim());
        return CompareObj(va, vb);
    }

    private static int CompareObj(object? a, object? b) => (a, b) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        (int ia, int ib) => ia.CompareTo(ib),
        (float fa, float fb) => fa.CompareTo(fb),
        (int ia, float fb) => ((float)ia).CompareTo(fb),
        (float fa, int ib) => fa.CompareTo(ib),
        (bool ba, bool bb) => ba.CompareTo(bb),
        (string sa, string sb) => string.Compare(sa, sb, StringComparison.Ordinal),
        _ => string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal)
    };

    private static object? ArithOp(object? a, object? b, char op) => (a, b, op) switch
    {
        (int ia, int ib, '+') => ia + ib,
        (float fa, float fb, '+') => fa + fb,
        (int ia, float fb, '+') => ia + fb,
        (float fa, int ib, '+') => fa + ib,
        (string sa, var sb, '+') => sa + (sb?.ToString() ?? ""),
        (var sa, string sb, '+') => (sa?.ToString() ?? "") + sb,

        (int ia, int ib, '-') => ia - ib,
        (float fa, float fb, '-') => fa - fb,
        (int ia, float fb, '-') => ia - fb,
        (float fa, int ib, '-') => fa - ib,

        (int ia, int ib, '*') => ia * ib,
        (float fa, float fb, '*') => fa * fb,
        (int ia, float fb, '*') => ia * fb,
        (float fa, int ib, '*') => fa * ib,

        (int ia, int ib, '/') when ib != 0 => ia / ib,
        (float fa, float fb, '/') when fb != 0 => fa / fb,
        (int ia, float fb, '/') when fb != 0 => ia / fb,
        (float fa, int ib, '/') when ib != 0 => fa / ib,

        _ => throw new InvalidOperationException($"Cannot {op} {a} and {b}")
    };

    private static object? Negate(object? a) => a switch
    {
        int i => -i,
        float f => -f,
        _ => throw new InvalidOperationException($"Cannot negate {a}")
    };

    private static bool IsTruthy(object? value) => value switch
    {
        bool b => b,
        int i => i != 0,
        float f => f != 0f,
        string s => !string.IsNullOrEmpty(s),
        null => false,
        _ => true
    };
}
