namespace GalNet.Core.Runtime;

/// <summary>Copies and validates one new run's save-only startup parameters.</summary>
public static class NewGameVariables
{
    public static IReadOnlyDictionary<string, object> Capture(IReadOnlyDictionary<string, object> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            if (!name.StartsWith("save.", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(name[5..]))
                throw new ArgumentException("New game variables require an explicit save. scope.", nameof(values));
            if (value is not (bool or int or string)
                && !(value is long integer && integer is >= int.MinValue and <= int.MaxValue)
                && !(value is float number && float.IsFinite(number))
                && !(value is double real && float.IsFinite((float)real)))
                throw new ArgumentException($"Unsupported initial value for '{name}'.", nameof(values));
            copy.Add(name, value);
        }
        return copy;
    }
}
