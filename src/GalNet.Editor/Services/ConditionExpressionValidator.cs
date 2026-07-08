using System;
using System.Collections.Generic;
using GalNet.Core.Variable;
using GalNet.Runtime.Variables;

namespace GalNet.Editor.Services;

public static class ConditionExpressionValidator
{
    public static bool TryValidate(
        string? expression,
        IEnumerable<ProjectVariableDefinition> variables,
        out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(expression))
            return true;

        try
        {
            var store = new VariableStore();
            foreach (var variable in variables)
                store.Set(variable.Name, GetDefaultValue(variable.DefaultValue));

            var evaluator = new ExpressionEvaluator(store);
            _ = evaluator.Evaluate(expression);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static object GetDefaultValue(Variable variable) => variable.Type switch
    {
        VariableType.Bool => variable.AsBool(),
        VariableType.Int => variable.AsInt(),
        VariableType.Float => variable.AsFloat(),
        _ => variable.AsString()
    };
}
