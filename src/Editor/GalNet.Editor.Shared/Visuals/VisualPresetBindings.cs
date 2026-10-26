using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Configuration;

namespace GalNet.Editor.Shared.Visuals;

public static class VisualPresetBindings
{
    public static (string Field, string Purpose)? Binding(string type, IReadOnlyDictionary<string, JsonElement> parameters) => type switch
    {
        BuiltinEntryIds.SpeedLinesPreset or BuiltinEntryIds.ImpactPreset => ("program", "speedLines"),
        BuiltinEntryIds.BlurTransition => ("blurProgram", "blur"),
        BuiltinEntryIds.RevealTransition => ("program", parameters.TryGetValue("mode", out var mode) && mode.ValueKind == JsonValueKind.String && mode.GetString() == "Mask" ? "maskReveal" : "reveal"),
        _ => null
    };

    /// <summary>Called only when creating/changing entry type. Never rewrites loaded or existing entries.</summary>
    public static Dictionary<string, JsonElement> Fill(string type, Dictionary<string, JsonElement> parameters, ProjectConfiguration? configuration)
    {
        if (configuration is null || Binding(type, parameters) is not { } binding) return parameters;
        if (parameters.TryGetValue(binding.Field, out var supplied) && supplied.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(supplied.GetString())) return parameters;
        if (configuration.ReadDefaultOptions().DefaultVisualPrograms.TryGetValue(binding.Purpose, out var program) && !string.IsNullOrWhiteSpace(program))
            parameters[binding.Field] = JsonSerializer.SerializeToElement(program);
        return parameters;
    }
}
