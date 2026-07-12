using System;
using System.Collections.Generic;
using GalNet.Core.Variable;

namespace GalNet.Editor.Abstraction.Services;

public interface IEditorPlayerVariableStore
{
    event Action? Changed;

    IReadOnlyDictionary<string, Variable> Snapshot { get; }

    void Reload();

    IReadOnlyDictionary<string, Variable> EnsureInitialized();

    void Reset();

    void SetValue(string name, object value);

    void SetVariable(string name, Variable variable);

    void Remove(string name);
}