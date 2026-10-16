using GalNet.Contracts.Builtins;
using GalNet.Presentation.Abstractions.Execution;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinVariableModule() : PrimitiveModule(
    registry => registry.Immediate<VariableSetOptions>(BuiltinEntryIds.SetVariable, (context, options) =>
            {
                var target = options.Target;
                if (!string.IsNullOrWhiteSpace(target))
                    context.Runtime.SetVariable(target, context.Runtime.EvaluateExpression(options.Expression ?? "") ?? "");
            }));
