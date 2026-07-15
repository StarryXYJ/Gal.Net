using NCalc;
using GalNet.Core.Variable;

namespace GalNet.Runtime.Variables;

/// <summary>
/// 条件/表达式求值器，基于 NCalc 引擎。
/// 变量通过占位符 [name] 引用，运行时动态解析为 NCalc 参数。
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
                double d => d != 0.0,
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
    /// 求值通用表达式。变量通过 [name] 语法引用，由 NCalc 参数机制动态解析。
    /// </summary>
    public object? Evaluate(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return null;

        var expr = new Expression(expression);
        expr.EvaluateParameter += ResolveParameter;

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
}
