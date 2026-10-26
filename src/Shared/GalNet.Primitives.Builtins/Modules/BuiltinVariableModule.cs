using GalNet.Contracts.Builtins;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Runtime.Logging;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinVariableModule() : PrimitiveModule(
    registry => registry.Immediate<VariableSetOptions>(BuiltinEntryIds.SetVariable, (context, options) =>
            {
                var target = options.Target;
                if (string.IsNullOrWhiteSpace(target)) return;
                object? value;
                try { value = context.Runtime.EvaluateExpression(options.Expression ?? ""); }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    GameLog.Logger.Warning(error, "{DiagnosticCode} at group {GroupId}, entry {EntryId}: assignment to {Variable} rejected",
                        "variable.expression.invalid", context.Runtime.CurrentNodeId, context.Entry.StableId, target);
                    return;
                }
                if (value is null)
                {
                    GameLog.Logger.Warning("{DiagnosticCode} at group {GroupId}, entry {EntryId}: assignment to {Variable} has no value",
                        "variable.expression.empty", context.Runtime.CurrentNodeId, context.Entry.StableId, target);
                    return;
                }
                context.Runtime.SetVariable(target, value);
            }));
