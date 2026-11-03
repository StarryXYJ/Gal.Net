using GalNet.Avalonia.GameHost;
using GalNet.Debugging;
using GalNet.Storage.FileSystem;

namespace GalNet.Avalonia.Debugging;

/// <summary>Owns installed debug revisions; classification and user decisions remain in Editor.</summary>
public sealed class DebugContentRevisionService(AvaloniaGameHostOptions options) :
    IDebugContentRevisionController, IGameResourceRevisionSource, IAsyncDisposable
{
    private sealed record Revision(string Id, GameResourceScope Resources);

    private readonly object _sync = new();
    private readonly SemaphoreSlim _install = new(1, 1);
    private readonly HashSet<Revision> _owned = [];
    private DebugIdentity? _identity;
    private Revision? _latest;
    private Revision? _run;
    private string _initialRevision = "";
    private bool _disposed;

    public DebugContentVersions Versions
    {
        get
        {
            lock (_sync)
            {
                var latest = _latest?.Id ?? _initialRevision;
                return new(latest, _run?.Id ?? _initialRevision);
            }
        }
    }

    public void SetIdentity(DebugIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_identity is not null && (!string.Equals(_identity.ProjectId, identity.ProjectId, StringComparison.Ordinal) ||
                !string.Equals(_identity.ContractFingerprint, identity.ContractFingerprint, StringComparison.Ordinal)))
                throw new InvalidOperationException("The debug content service is already bound to another project identity.");
            _identity = identity;
        }
    }

    public GameResourceScope SelectForNewRun(GameResourceScope initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureInitialRevision(initial);
            if (_latest is null)
            {
                _run = null;
                return initial;
            }
            _run = _latest;
            return _latest.Resources;
        }
    }

    public async Task<DebugContentVersions> InstallAsync(DebugContentInstallRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Revision);
        await _install.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DebugIdentity identity;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                identity = _identity ?? throw new InvalidOperationException("The debug connection has no content identity.");
                if (!string.Equals(identity.ContractFingerprint, request.ContractFingerprint, StringComparison.Ordinal))
                    throw new InvalidOperationException("Project contracts changed; restart the debug application.");
                if (string.Equals(_latest?.Id ?? _initialRevision, request.Revision, StringComparison.Ordinal)) return Versions;
            }

            var directory = Path.GetFullPath(request.Directory);
            var manifest = DebugContentManifestStore.Read(directory);
            Validate(request, manifest, identity);
            var catalogs = options.CreateContentCatalogs();
            var resources = await GameResourceScope.OpenAsync(directory, catalogs.Resources, catalogs.Gallery,
                options.Content, cancellationToken).ConfigureAwait(false);
            Revision? discard = null;
            try
            {
                options.ConfigureDecoders(resources.Assets);
                var fingerprint = DebugContentIdentity.Compute(directory);
                if (!string.Equals(fingerprint, request.ContentFingerprint, StringComparison.Ordinal))
                    throw new InvalidDataException("Installed debug content fingerprint does not match the request.");
                var candidate = new Revision(request.Revision, resources);
                resources = null!;
                lock (_sync)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (string.Equals(_latest?.Id ?? _initialRevision, request.Revision, StringComparison.Ordinal))
                        discard = candidate;
                    else
                    {
                        if (_latest is not null && !ReferenceEquals(_latest, _run))
                        {
                            discard = _latest;
                            _owned.Remove(_latest);
                        }
                        _latest = candidate;
                        _owned.Add(candidate);
                    }
                }
            }
            finally { resources?.Dispose(); }
            discard?.Resources.Dispose();
            return Versions;
        }
        finally { _install.Release(); }
    }

    public GalNet.Core.Graph.Graph GetLatestGraph(string revision)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_latest is null || !string.Equals(_latest.Id, revision, StringComparison.Ordinal))
                throw new InvalidOperationException("The requested content revision is not the installed latest revision.");
            return _latest.Resources.Content.Graph;
        }
    }

    public void CommitActivation(string revision)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_latest is null || !string.Equals(_latest.Id, revision, StringComparison.Ordinal))
                throw new InvalidOperationException("The activated content is no longer the installed latest revision.");
            _run = _latest;
        }
    }

    private void EnsureInitialRevision(GameResourceScope initial)
    {
        if (!string.IsNullOrEmpty(_initialRevision)) return;
        var path = Path.Combine(initial.GameDirectory,
            DebugContentManifest.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        _initialRevision = File.Exists(path)
            ? DebugContentManifestStore.Read(initial.GameDirectory).Revision
            : _identity?.ContentFingerprint ?? "initial";
    }

    private static void Validate(DebugContentInstallRequest request, DebugContentManifest manifest, DebugIdentity identity)
    {
        if (!string.Equals(manifest.ProjectId, identity.ProjectId, StringComparison.Ordinal))
            throw new InvalidDataException("Debug content belongs to a different project.");
        if (!string.Equals(manifest.Revision, request.Revision, StringComparison.Ordinal) ||
            !string.Equals(manifest.ContentFingerprint, request.ContentFingerprint, StringComparison.Ordinal) ||
            !string.Equals(manifest.ContractFingerprint, request.ContractFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("Debug content manifest does not match the install request.");
        if (!string.Equals(identity.ContractFingerprint, request.ContractFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("Project contracts changed; restart the debug application.");
    }

    public async ValueTask DisposeAsync()
    {
        await _install.WaitAsync().ConfigureAwait(false);
        Revision[] revisions;
        try
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                revisions = _owned.ToArray();
                _owned.Clear();
                _latest = null;
                _run = null;
            }
            foreach (var revision in revisions) revision.Resources.Dispose();
        }
        finally
        {
            _install.Release();
        }
    }
}
