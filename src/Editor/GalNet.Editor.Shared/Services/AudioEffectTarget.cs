using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Contracts.Builtins.Audio;

namespace GalNet.Editor.Shared.Services;

public sealed record AudioEffectKey(SEffectScope Scope, string TargetId, string InstanceId);
public sealed record AudioEffectTarget(AudioEffectKey Key, TargetPresence Presence,
    IReadOnlyList<SceneLocator> Sources, IReadOnlyList<string> Types, bool MetadataComplete = true);

/// <summary>Attachment qualification shared by identity copying, indexing and position analysis.</summary>
public static class AudioAuthoringTarget
{
    public static string? Qualifier(JsonObject owner)
    {
        try
        {
            var scope = owner["scope"]?.GetValue<string>() ?? "track";
            var target = owner["targetId"]?.GetValue<string>();
            return scope is "track" or "bus" && !string.IsNullOrWhiteSpace(target)
                ? JsonSerializer.Serialize(new[] { scope, target }) : null;
        }
        catch (InvalidOperationException) { return null; }
    }
}
