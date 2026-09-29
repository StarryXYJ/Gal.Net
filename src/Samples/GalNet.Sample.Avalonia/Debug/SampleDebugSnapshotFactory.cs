using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.Variable;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Sample.Avalonia.Debug;

/// <summary>Creates reduced debug snapshots without mutating the live engine snapshot.</summary>
internal static class SampleDebugSnapshotFactory
{
    public static GameSnapshot Create(GameSnapshot source, SampleDebugSaveKind kind)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new GameSnapshot
        {
            Version = source.Version,
            NodeId = source.NodeId,
            EntryIndex = kind == SampleDebugSaveKind.GalGroupStart ? 0 : source.EntryIndex,
            Variables = source.Variables.ToDictionary(pair => pair.Key, pair => CloneVariable(pair.Value)),
            SceneState = new SceneState()
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
