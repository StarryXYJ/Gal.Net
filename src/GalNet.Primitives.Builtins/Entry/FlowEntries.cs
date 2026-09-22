namespace GalNet.Core.Entry;

public sealed class WaitEntry : PrimitiveEntry { public const string TypeId = "wait"; public override string Type => TypeId; public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("duration", EntryParameterType.Float)); public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("duration", "1")); }
public sealed class SetVariableEntry : PrimitiveEntry { public const string TypeId = "variable.set"; public override string Type => TypeId; public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("target", EntryParameterType.VariableName), ("expression", EntryParameterType.Expression)); }
