using GalNet.Core.Services;
using GalNet.Core.Entry;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using System.Text.Json;

namespace GalNet.Runtime.Handlers;

/// <summary>State-only context for one entry execution.</summary>
public sealed class EntryContext
{
    private static readonly JsonSerializerOptions LayerTransformJsonOptions = new() { PropertyNameCaseInsensitive = true };
    public required Entry Entry { get; init; }
    public required IGameRuntime Runtime { get; init; }

    public Dictionary<string, string> Params => Entry.Values;
    public ITextResolver TextResolver => Runtime.TextResolver;

    public string GetString(string key, string def = "") => Params.TryGetValue(key, out var value) ? value : def;
    public bool GetBool(string key, bool def = false) => Params.TryGetValue(key, out var value) && bool.TryParse(value, out var result) ? result : def;
    public float GetFloat(string key, float def = 0f) => Params.TryGetValue(key, out var value) && float.TryParse(value, out var result) ? result : def;
    public int GetInt(string key, int def = 0) => Params.TryGetValue(key, out var value) && int.TryParse(value, out var result) ? result : def;
    public string GetText(string key, string def = "") => TextResolver.Resolve(GetString(key, def));

    public LayerTransform GetLayerTransform(string key = "transform")
    {
        var fallback = new LayerTransform();
        var raw = GetString(key);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        try
        {
            var transform = JsonSerializer.Deserialize<LayerTransform>(raw, LayerTransformJsonOptions);
            if (transform is null || transform.ScaleX <= 0 || transform.ScaleY <= 0)
                throw new InvalidDataException("Layer scale values must be greater than zero.");
            return transform;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Invalid layer transform: {raw}", exception);
        }
    }
}
