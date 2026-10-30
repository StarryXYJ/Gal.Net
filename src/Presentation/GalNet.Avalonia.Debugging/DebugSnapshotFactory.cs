using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Runtime;
using GalNet.Core.Variable;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Avalonia.Debugging;

/// <summary>Creates reduced debug snapshots without mutating the live engine snapshot.</summary>
public static class DebugSnapshotFactory
{
    public static GameSnapshot Create(GameSnapshot source, DebugSaveKind kind)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new GameSnapshot
        {
            Version = source.Version,
            ContentBuildId = source.ContentBuildId,
            StoryPackages = source.StoryPackages.ToArray(),
            NodeId = source.NodeId,
            EntryIndex = kind == DebugSaveKind.GalGroupStart ? 0 : source.EntryIndex,
            Variables = source.Variables.ToDictionary(pair => pair.Key, pair => CloneVariable(pair.Value)),
            States = source.States.ToDictionary(pair => pair.Key, pair =>
                new StateSection(pair.Value.Version, System.Text.Json.JsonSerializer.SerializeToElement(Array.Empty<object>())))
        };
    }

    private static GalVariable CloneVariable(GalVariable variable)
    {
        var copy = new GalVariable { Uid = variable.Uid, Name = variable.Name };
        switch (variable.Type)
        {
            case VariableType.Bool: copy.SetValue(variable.AsBool()); break;
            case VariableType.Int: copy.SetValue(variable.AsInt()); break;
            case VariableType.Float: copy.SetValue(variable.AsFloat()); break;
            default: copy.SetValue(variable.AsString()); break;
        }
        return copy;
    }
}
