using GalNet.Core.Entry;

namespace GalNet.Contracts.Builtins;

public sealed class WaitEntry : PrimitiveEntry { public const string TypeId = "flow.wait"; public override string Type => TypeId; }
public sealed class SetVariableEntry : PrimitiveEntry { public const string TypeId = "variable.set"; public override string Type => TypeId; public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("target", EntryParameterType.VariableName), ("expression", EntryParameterType.Expression)); }
