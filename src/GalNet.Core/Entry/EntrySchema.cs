namespace GalNet.Core.Entry;

internal static class EntrySchema
{
    public static IReadOnlyDictionary<string, EntryParameterType> Parameters(params (string Name, EntryParameterType Type)[] items) =>
        new Dictionary<string, EntryParameterType>(items.ToDictionary(x => x.Name, x => x.Type), StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string> Defaults(params (string Name, string Value)[] items) =>
        new Dictionary<string, string>(items.ToDictionary(x => x.Name, x => x.Value), StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Options(params (string Name, string[] Values)[] items) =>
        new Dictionary<string, IReadOnlyList<string>>(items.ToDictionary(x => x.Name, x => (IReadOnlyList<string>)x.Values), StringComparer.Ordinal);
}
