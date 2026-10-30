using GalNet.Contracts.Builtins;
using GalNet.Runtime.Engine;

namespace GalNet.Avalonia.Debugging;

/// <summary>Host-owned restart/platform actions and project-specific replay choices.</summary>
public sealed class GameDebugHostOptions
{
    public required Func<CancellationToken, Task> RestartAsync { get; init; }
    public Action? OpenLogDirectory { get; init; }
    public IReadOnlyDictionary<string, DebugReplayBehavior> Instructions { get; init; } = CreateBuiltinReplayPolicy();

    public static Dictionary<string, DebugReplayBehavior> CreateBuiltinReplayPolicy()
    {
        var instructions = BuiltinSchemas.CreateAllModules().SelectMany(module => module.PrimitiveEntries.Values)
            .Where(entry => entry.Name != BuiltinEntryIds.InvokeCustom)
            .ToDictionary(entry => entry.Name, _ => DebugReplayBehavior.Scene, StringComparer.Ordinal);
        instructions[BuiltinEntryIds.SetVariable] = DebugReplayBehavior.Ignore;
        instructions[BuiltinEntryIds.UnlockGallery] = DebugReplayBehavior.Ignore;
        return instructions;
    }
}
