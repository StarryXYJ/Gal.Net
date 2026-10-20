using System;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.History;

/// <summary>One content history for the entire project; page state is never recorded here.</summary>
public sealed class EditorHistories : IProjectEditLifetime, IDisposable
{
    public UndoRedoHistory Content { get; }
    private readonly IProjectContentSession? _session;
    private readonly IDisposable? _captureRegistration;
    public bool IsDirty => Content.IsDirty;
    public event Action? Changed { add => Content.Changed += value; remove => Content.Changed -= value; }

    public EditorHistories(IProjectContentSession? session = null, ProjectSessionOptions? options = null)
    {
        Content = new((options ?? new()).HistorySteps, (options ?? new()).RetainedByteBudget);
        _session = session;
        if (session is not null)
        {
            Content.BindContent(() => session.IsDirty, () => session.RepairRequired);
            _captureRegistration = session.RegisterCaptureGuard(Content.AcquireCaptureAsync);
            Content.Changed += OnContentChanged;
        }
    }

    public void Clear() => Content.Clear();
    public System.Threading.Tasks.Task SuspendAsync() => Content.SuspendAsync();
    public void ResumeEditing() => Content.Resume();
    public void Dispose()
    {
        Content.Changed -= OnContentChanged;
        _captureRegistration?.Dispose();
        Content.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnContentChanged()
    {
        if (Content.RepairRequired) _session?.BlockWrites(new InvalidOperationException("History compensation failed. Reopen the project after inspecting recovery material."));
        _session?.Refresh();
    }
}
