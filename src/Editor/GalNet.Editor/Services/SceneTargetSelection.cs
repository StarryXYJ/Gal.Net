using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Services;

/// <summary>One reusable selection lease. Consumers decide how the resolved handle is committed.</summary>
public sealed class SceneTargetSelection(SceneTargetCatalog catalog, SceneTargetSnapshot snapshot, string? noneLabelKey = null) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    public SceneTargetSnapshot Snapshot { get; private set; } = snapshot;
    public IEditorLocalizationService Text => catalog.Text;
    public string? SelectedId { get; private set; }
    public SceneTarget? Target { get; private set; }
    public bool IsCurrent => !_disposed && catalog.IsCurrent(Snapshot);
    public IReadOnlyList<ResourcePickerItem> Items => (noneLabelKey is null ? Array.Empty<ResourcePickerItem>() : [new("none", Text[noneLabelKey], "", "×")])
        .Concat(Snapshot.Choices.Select(choice => choice.Item)).ToArray();
    public async Task PrepareAsync()
    {
        if (!IsCurrent) return;
        await catalog.PrepareAsync(Snapshot.GroupId, Snapshot.EntryId, Snapshot.Position, _lifetime.Token);
        RefreshLabels();
    }
    public void RefreshLabels()
    {
        if (!IsCurrent) return;
        Snapshot = catalog.Query(Snapshot.GroupId, Snapshot.EntryId, Snapshot.Filter, Snapshot.Position);
    }
    public bool Select(string id)
    {
        if (!IsCurrent) return false;
        if (id == "none" && noneLabelKey is not null) { SelectedId = id; Target = null; return true; }
        var resolved = catalog.Resolve(Snapshot, id); if (resolved is null) return false;
        SelectedId = id; Target = resolved; return true;
    }
    public void Clear() { SelectedId = null; Target = null; }
    public bool TryResolve(out string handle)
    {
        handle = "";
        if (SelectedId is null || !Select(SelectedId)) return false;
        handle = Target?.Handle ?? ""; return true;
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); }
}
