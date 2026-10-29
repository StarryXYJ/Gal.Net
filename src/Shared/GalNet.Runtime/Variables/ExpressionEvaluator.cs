using System.Text;
using System.Text.Json;
using GalNet.Core.Variable;
using GalNet.Runtime.Logging;
using NCalc;

namespace GalNet.Runtime.Variables;

/// <summary>
/// 条件/表达式求值器，基于 NCalc 引擎。
/// 变量通过占位符 [name] 引用，运行时动态解析为 NCalc 参数。
/// 支持的运算：比较 (== != < > <= >=)、逻辑 (&& ||)、算术 (+ - * /)。
/// </summary>
public sealed class ExpressionEvaluator
{
    private readonly VariableStore _store;
    private readonly Action<string, Exception> _diagnostic;
    private readonly HashSet<string> _reportedConditions = new(StringComparer.Ordinal);

    public ExpressionEvaluator(VariableStore store, Action<string, Exception>? diagnostic = null)
    {
        _store = store;
        _diagnostic = diagnostic ?? ((expression, error) => GameLog.Logger.Warning(error,
            "{DiagnosticCode}: condition {Expression} evaluates to false", "expression.invalid", expression));
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
                double d => d != 0.0,
                string s => !string.IsNullOrEmpty(s),
                null => false,
                _ => true
            };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Invalid content may be reached repeatedly by a story loop. Bound both diagnostics and storage.
            bool report;
            lock (_reportedConditions)
                report = _reportedConditions.Count < 64 && _reportedConditions.Add(expression);
            if (report)
                try { _diagnostic(expression, error); }
                catch (Exception) { /* Diagnostic observers cannot change condition semantics. */ }
            return false;
        }
    }

    /// <summary>
    /// 求值通用表达式。变量通过 [name] 语法引用，由 NCalc 参数机制动态解析。
    /// </summary>
    public object? Evaluate(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return null;

        var literals = new Dictionary<string, string>(StringComparer.Ordinal);
        var expr = new Expression(BindStringLiterals(expression, literals));
        expr.EvaluateParameter += (name, args) =>
        {
            if (literals.TryGetValue(name, out var literal)) args.Result = literal;
            else ResolveParameter(name, args);
        };

        return expr.Evaluate();
    }

    private void ResolveParameter(string name, ParameterArgs args)
    {
        if (_store.TryGet(name, out var v))
            args.Result = CoerceForNCalc(v);
        else
            args.Result = null;
    }

    private static object? CoerceForNCalc(Variable v) => v.Type switch
    {
        VariableType.Bool => v.AsBool(),
        VariableType.Int => v.AsInt(),
        VariableType.Float => v.AsFloat(),
        VariableType.String => v.AsString(),
        _ => v.AsString()
    };

    // Bind JSON string literals as values; never rewrite apostrophes, escapes or variable names.
    private static string BindStringLiterals(string expression, Dictionary<string, string> literals)
    {
        var result = new StringBuilder(expression.Length);
        for (var index = 0; index < expression.Length; index++)
        {
            var current = expression[index];
            if (current is '\'' or '[' or '#')
            {
                var end = current == '[' ? ']' : current;
                result.Append(current);
                while (++index < expression.Length)
                {
                    current = expression[index];
                    result.Append(current);
                    if (current == '\\' && end == '\'' && index + 1 < expression.Length)
                        result.Append(expression[++index]);
                    else if (current == end) break;
                }
            }
            else if (current == '"')
            {
                var start = index;
                while (++index < expression.Length)
                {
                    if (expression[index] == '\\') index++;
                    else if (expression[index] == '"') break;
                }
                if (index >= expression.Length) throw new FormatException("Unterminated expression string.");
                using var literalDocument = JsonDocument.Parse(expression[start..(index + 1)]);
                var literal = literalDocument.RootElement.GetString()!;
                var suffix = literals.Count;
                string name;
                do { name = $"__gal_literal_{suffix++}"; }
                while (literals.ContainsKey(name) || expression.Contains($"[{name}]", StringComparison.Ordinal));
                literals.Add(name, literal);
                result.Append('[').Append(name).Append(']');
            }
            else result.Append(current);
        }
        return result.ToString();
    }
}
