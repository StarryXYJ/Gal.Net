using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinVariableModule : PrimitiveModule
{
    public BuiltinVariableModule() : base(
    [
        BuiltinInstructionFactory.Create<SetVariableEntry>(context => new ImmediatePrimitiveInstance(() =>
            {
                var target = Arguments.String(context, "target");
                if (!string.IsNullOrWhiteSpace(target))
                    context.Runtime.SetVariable(target, context.Runtime.EvaluateExpression(Arguments.String(context, "expression")) ?? "");
            }, batchId: context.BatchId))
    ])
    { }
}
