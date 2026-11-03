using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GalNet.Debugging;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Runtime.Engine;

namespace GalNet.Editor.Services;

/// <summary>Builds complete candidates and decides how the connected game may apply them.</summary>
public sealed class DebugContentHotReloadService(
    ExternalGameDebugClient client,
    ProjectDebugPreparation preparation,
    IEditorDocumentService documents)
{
    private ProjectContentBuildResult? _pendingBuild;
    private DebugContentChangeScope? _pendingChanges;

    public event Action? Changed;
    public ProjectContentBuildResult? PublishedBuild { get; private set; }
    public DebugContentImpact? PendingImpact { get; private set; }
    public bool HasPending => _pendingBuild is not null;

    public async Task<ProjectContentBuildResult> PrepareAsync(Action<string>? reportStage = null)
    {
        var build = await preparation.PrepareAsync(reportStage).ConfigureAwait(false);
        PublishedBuild = build;
        ClearPending();
        Changed?.Invoke();
        return build;
    }

    public async Task<DebugContentImpact?> BuildAndApplyAsync(DebugContentChangeScope changes,
        Action<string>? reportStage = null, CancellationToken cancellationToken = default)
    {
        var candidate = await preparation.BuildAsync(reportStage).ConfigureAwait(false);
        if (!IsCurrent(candidate)) return null;
        var manifest = candidate.Manifest ?? throw new InvalidDataException("Debug build did not produce a content manifest.");
        RequireCapability("content.install");
        await client.SendAsync("content.install", new DebugContentInstallRequest(candidate.OutputPath,
            manifest.ContentFingerprint, manifest.ContractFingerprint), cancellationToken: cancellationToken).ConfigureAwait(false);

        var impact = Classify(candidate, changes);
        if (impact == DebugContentImpact.Direct)
            await ActivateAsync(candidate, returnToGroupStart: false, cancellationToken).ConfigureAwait(false);
        else
            SetPending(candidate, changes, impact);
        return impact;
    }

    public async Task<DebugContentImpact> ApplyPendingAsync(CancellationToken cancellationToken = default)
    {
        var candidate = _pendingBuild ?? throw new InvalidOperationException("There is no pending debug content update.");
        var changes = _pendingChanges ?? throw new InvalidOperationException("There is no pending debug content scope.");
        var impact = Classify(candidate, changes);
        if (impact == DebugContentImpact.RestartGame)
        {
            SetPending(candidate, changes, impact);
            return impact;
        }

        await ActivateAsync(candidate, impact == DebugContentImpact.ReturnGroupStart, cancellationToken).ConfigureAwait(false);
        return DebugContentImpact.Direct;
    }

    public async Task<DebugContentImpact> RestartGameAsync(CancellationToken cancellationToken = default)
    {
        var candidate = _pendingBuild ?? throw new InvalidOperationException("There is no pending debug content update.");
        var changes = _pendingChanges ?? throw new InvalidOperationException("There is no pending debug content scope.");
        if (Classify(candidate, changes) != DebugContentImpact.RestartGame)
        {
            return await ApplyPendingAsync(cancellationToken).ConfigureAwait(false);
        }

        await client.SendAsync("stop", cancellationToken: cancellationToken).ConfigureAwait(false);
        await client.SendAsync("start", cancellationToken: cancellationToken).ConfigureAwait(false);
        Commit(candidate);
        return DebugContentImpact.Direct;
    }

    private DebugContentImpact Classify(ProjectContentBuildResult candidate, DebugContentChangeScope changes)
    {
        var current = PublishedBuild?.Manifest ?? throw new InvalidOperationException("No running debug content is available.");
        if (string.Equals(current.ContentFingerprint, candidate.Manifest!.ContentFingerprint, StringComparison.Ordinal))
            return DebugContentImpact.Direct;
        if (changes.Global) return DebugContentImpact.RestartGame;
        var state = client.State ?? throw new InvalidOperationException("No active game state is available.");
        var position = new DebugContentPosition(state.NodeId, state.EntryIndex, state.ActiveEntryIds,
            state.VisitedGroups ?? []);
        var executed = DebugContentImpactClassifier.ExecutedAuthorEntryCount(current, position);
        var visited = position.VisitedGroups.ToHashSet(StringComparer.Ordinal);
        var impact = DebugContentImpact.Direct;
        foreach (var change in changes.Groups)
        {
            if (string.Equals(change.GroupId, position.CurrentGroupId, StringComparison.Ordinal))
            {
                if (change.FirstIndex < executed) impact = DebugContentImpact.ReturnGroupStart;
            }
            else if (visited.Contains(change.GroupId)) return DebugContentImpact.RestartGame;
        }
        return impact == DebugContentImpact.ReturnGroupStart && !state.CachedGroups.Contains(state.NodeId)
            ? DebugContentImpact.RestartGame
            : impact;
    }

    private async Task ActivateAsync(ProjectContentBuildResult candidate, bool returnToGroupStart,
        CancellationToken cancellationToken)
    {
        var current = PublishedBuild?.Manifest ?? throw new InvalidOperationException("No running debug content is available.");
        var state = client.State ?? throw new InvalidOperationException("No active game state is available.");
        var run = client.Host?.Run ?? throw new InvalidOperationException("No active game is attached.");
        var method = returnToGroupStart ? "content.activateAndSeekGroupStart" : "content.activate";
        RequireCapability(method);
        var position = new DebugContentPosition(state.NodeId, state.EntryIndex, state.ActiveEntryIds,
            state.VisitedGroups ?? []);
        var candidateIndex = returnToGroupStart ? 0
            : DebugContentImpactClassifier.MapCandidateEntryIndex(current, candidate.Manifest!, position);
        var request = new DebugContentActivateRequest(
            new GameDebugContentBoundary(state.NodeId, state.EntryIndex, state.ActiveEntryIds), candidateIndex);
        await client.SendAsync(method, request, run.RunId, run.Generation, cancellationToken).ConfigureAwait(false);
        Commit(candidate);
    }

    private bool IsCurrent(ProjectContentBuildResult build) =>
        string.Equals(build.AuthorSignature,
            ProjectAuthorIdentity.Compute(documents.CurrentDocument, documents.LoadedProject.GroupEntries), StringComparison.Ordinal);

    private void RequireCapability(string method)
    {
        if (client.Host?.Run?.Capabilities.Contains(method) != true)
            throw new NotSupportedException($"The debug App does not support '{method}'. Restart it with the current Developer build.");
    }

    private void SetPending(ProjectContentBuildResult build, DebugContentChangeScope changes, DebugContentImpact impact)
    {
        _pendingBuild = build;
        _pendingChanges = changes;
        PendingImpact = impact;
        Changed?.Invoke();
    }

    private void Commit(ProjectContentBuildResult build)
    {
        PublishedBuild = build;
        ClearPending();
        Changed?.Invoke();
    }

    private void ClearPending()
    {
        _pendingBuild = null;
        _pendingChanges = null;
        PendingImpact = null;
    }
}

public sealed record DebugContentChangeScope(bool Global, IReadOnlyList<AuthorGroupChange> Groups);
