using GalNet.Contracts.Builtins;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinVariableModule : PrimitiveModule
{
    public BuiltinVariableModule() : base(
    [
        PrimitiveImplementation.Immediate<VariableSetOptions>(SetVariableEntry.TypeId, (context, options) =>
            {
                var target = options.Target;
                if (!string.IsNullOrWhiteSpace(target))
                    context.Runtime.SetVariable(target, context.Runtime.EvaluateExpression(options.Expression ?? "") ?? "");
            })
    ])
    { }
}
