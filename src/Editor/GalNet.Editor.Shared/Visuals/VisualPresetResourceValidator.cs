using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Visuals;

/// <summary>Resource validation at the project boundary; the pure entry compiler does not load files.</summary>
internal sealed class VisualPresetResourceValidator(string root)
{
    private Dictionary<string, (string Path, string Type)>? _assets;
    private readonly ProjectRawReader _reader = new(root);

    public void Validate(string type, IReadOnlyDictionary<string, JsonElement> parameters)
    {
        if (VisualPresetBindings.Binding(type, parameters) is not { } binding) return;
        if (!parameters.TryGetValue(binding.Field, out var program) || program.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(program.GetString()))
            throw new InvalidDataException($"Preset requires an explicit '{binding.Field}' resource ID.");
        var asset = Asset(program.GetString()!);
        if (asset.Type != "effect-program") throw new InvalidDataException("Preset shader must reference an effect-program asset.");
        var parsed = ShaderEffectMetadataParser.Parse(new(program.GetString()!), File.ReadAllText(_reader.Resolve(asset.Path)));
        if (!parsed.Success) throw new InvalidDataException(string.Join("; ", parsed.Diagnostics));
        var descriptor = parsed.Descriptor!;
        if (!descriptor.Supports(EffectStage.Layer)) throw new InvalidDataException("This preset requires a layer shader.");
        void Need(string name, ShaderEffectParameterKind kind, bool animate = false)
        {
            var parameter = descriptor.Parameters.SingleOrDefault(parameter => parameter.Name == name);
            if (parameter is null || parameter.Kind != kind || animate && !parameter.Animatable)
                throw new InvalidDataException($"Preset shader requires {(animate ? "animatable " : "")}{kind} parameter '{name}'.");
        }
        float Number(string name, float fallback) => parameters.TryGetValue(name, out var value) && value.TryGetSingle(out var number) ? number : fallback;
        void Range(string name, float minimum, float maximum)
        {
            var parameter = descriptor.Parameters.Single(parameter => parameter.Name == name);
            if (parameter.Minimum is { } lower && minimum < lower || parameter.Maximum is { } upper && maximum > upper)
                throw new InvalidDataException($"Preset values exceed shader parameter '{name}' range.");
        }
        if (type is BuiltinEntryIds.SpeedLinesPreset or BuiltinEntryIds.ImpactPreset)
        {
            Need("time", ShaderEffectParameterKind.Float, true);
            Need("speed", ShaderEffectParameterKind.Float);
            Need("flicker", ShaderEffectParameterKind.Float);
            Range("time", 0, Number("durationFrames", 36) / Number("frameRate", 60));
            Range("speed", Number("speed", 2), Number("speed", 2));
            Range("flicker", Number("flicker", .3f), Number("flicker", .3f));
        }
        else if (type == BuiltinEntryIds.BlurTransition)
        {
            Need("radius", ShaderEffectParameterKind.Float, true);
            Need("direction", ShaderEffectParameterKind.Vector2);
            Range("radius", 0, Number("radius", 24));
        }
        else
        {
            Need("progress", ShaderEffectParameterKind.Float, true);
            Need("softness", ShaderEffectParameterKind.Float);
            Range("progress", 0, 1);
            if (binding.Purpose == "maskReveal")
            {
                Need("mask", ShaderEffectParameterKind.Texture);
                if (!parameters.TryGetValue("maskAsset", out var mask) || mask.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(mask.GetString()))
                    throw new InvalidDataException("Mask reveal requires maskAsset.");
                if (Asset(mask.GetString()!).Type != "sprite") throw new InvalidDataException("Mask must reference a sprite asset.");
            }
            else
            {
                Need("mode", ShaderEffectParameterKind.Enum);
                var requested = parameters.TryGetValue("mode", out var mode) ? mode.GetString() : "Wipe";
                if (descriptor.Parameters.Single(parameter => parameter.Name == "mode").Options?.Contains(requested, StringComparer.Ordinal) != true)
                    throw new InvalidDataException($"Reveal shader does not support mode '{requested}'.");
                Need("angle", ShaderEffectParameterKind.Float); Need("count", ShaderEffectParameterKind.Float); Need("center", ShaderEffectParameterKind.Vector2);
            }
        }
    }

    private (string Path, string Type) Asset(string id)
    {
        if (_assets is null)
        {
            _assets = new(StringComparer.OrdinalIgnoreCase);
            var diagnostics = new List<GalNet.Editor.Abstraction.Commands.ToolDiagnostic>();
            foreach (var file in _reader.Files("Assets", diagnostics).Where(file => file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            {
                var meta = _reader.Read(file, diagnostics);
                if (meta is null) continue;
                var identity = ProjectRawReader.MetadataIdentity(meta.Value);
                if (Guid.TryParse(identity.Id, out var guid) && meta.Value.TryGetProperty("type", out var type))
                {
                    if (!_assets.TryAdd(guid.ToString("N"), (file[..^5], type.GetString() ?? ""))) throw new InvalidDataException("Duplicate resource IDs.");
                }
            }
            if (diagnostics.Any(diagnostic => diagnostic.Severity == "error")) throw new InvalidDataException("Preset resource inventory is unreadable.");
        }
        if (!Guid.TryParse(id, out var resourceId) || !_assets.TryGetValue(resourceId.ToString("N"), out var asset) || !File.Exists(_reader.Resolve(asset.Path)))
            throw new InvalidDataException($"Preset resource '{id}' is missing.");
        return asset;
    }
}
