using System.Text.Json;

namespace GalNet.Contracts.Builtins.Audio;

public static class SEffectParameters
{
    /// <summary>Numeric effect patches replace explicit fields and preserve all other values.</summary>
    public static JsonElement Merge(JsonElement current, JsonElement patch)
    {
        if (current.ValueKind != JsonValueKind.Object || patch.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Audio effect parameters must be an object.");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in current.EnumerateObject())
            if (!values.TryAdd(item.Name, item.Value)) throw new InvalidDataException("Duplicate audio effect parameter.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in patch.EnumerateObject())
        {
            if (!seen.Add(item.Name)) throw new InvalidDataException("Duplicate audio effect parameter.");
            values[item.Name] = item.Value;
        }
        return JsonSerializer.SerializeToElement(values);
    }
}

public static class SEffectDefaults
{
    public const float LowPassCutoff = 20_000, HighPassCutoff = 20, Q = .7071068f, Gain = 1;
    public const float RoomSize = .5f, DecaySeconds = 1.5f, Damping = .4f, Mix = .25f;
}
