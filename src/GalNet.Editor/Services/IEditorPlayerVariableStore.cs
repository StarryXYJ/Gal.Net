using System;
using System.Collections.Generic;
using GalNet.Core.Settings;
using GalNet.Core.Variable;

namespace GalNet.Editor.Services;

public interface IEditorPlayerVariableStore
{
    event Action? Changed;

    IReadOnlyDictionary<string, Variable> Snapshot { get; }

    void Reload();

    IReadOnlyDictionary<string, Variable> EnsureInitialized(ProjectSettings settings);

    void Reset(ProjectSettings settings);

    void SetValue(string name, object value);

    void SetVariable(string name, Variable variable);

    void Remove(string name);
}
