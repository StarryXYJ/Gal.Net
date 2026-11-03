using GalNet.Avalonia.GameHost;
using GalNet.Debugging;
using GalNet.Storage.FileSystem;

namespace GalNet.Avalonia.Debugging;

/// <summary>Owns the running and latest complete debug content; Editor decides when latest becomes active.</summary>
public sealed class DebugContentUpdateService(AvaloniaGameHostOptions options) :
    IDebugContentController, IGameResourceUpdateSource, IAsyncDisposable
{
    private sealed record Candidate(GameResourceScope Resources);

    private readonly object _sync = new();
    private readonly SemaphoreSlim _install = new(1, 1);
    private readonly HashSet<Candidate> _owned = [];
    private DebugIdentity? _identity;
    private Candidate? _latest;
    private Candidate? _run;
    private bool _disposed;

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
            _run = _latest;
            return _run?.Resources ?? initial;
        }
    }

    public async Task InstallAsync(DebugContentInstallRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Directory);
        await _install.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DebugIdentity identity;
            var directory = Path.GetFullPath(request.Directory);
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                identity = _identity ?? throw new InvalidOperationException("The debug connection has no content identity.");
                if (!string.Equals(identity.ContractFingerprint, request.ContractFingerprint, StringComparison.Ordinal))
                    throw new InvalidOperationException("Project contracts changed; restart the debug application.");
                if (_latest is not null && string.Equals(_latest.Resources.GameDirectory, directory, StringComparison.OrdinalIgnoreCase)) return;
            }

            var manifest = DebugContentManifestStore.Read(directory);
            Validate(request, manifest, identity);
            var catalogs = options.CreateContentCatalogs();
            var resources = await GameResourceScope.OpenAsync(directory, catalogs.Resources, catalogs.Gallery,
                options.Content, cancellationToken).ConfigureAwait(false);
            Candidate? discard = null;
            try
            {
                options.ConfigureDecoders(resources.Assets);
                if (!string.Equals(DebugContentIdentity.Compute(directory), request.ContentFingerprint, StringComparison.Ordinal))
                    throw new InvalidDataException("Installed debug content fingerprint does not match the request.");
                var candidate = new Candidate(resources);
                resources = null!;
                lock (_sync)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (_latest is not null && string.Equals(_latest.Resources.GameDirectory, directory, StringComparison.OrdinalIgnoreCase))
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
        }
        finally { _install.Release(); }
    }

    public GalNet.Core.Graph.Graph GetLatestGraph()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return (_latest ?? throw new InvalidOperationException("No debug content candidate is installed.")).Resources.Content.Graph;
        }
    }

    public void CommitActivation()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _run = _latest ?? throw new InvalidOperationException("No debug content candidate is installed.");
        }
    }

    private static void Validate(DebugContentInstallRequest request, DebugContentManifest manifest, DebugIdentity identity)
    {
        if (!string.Equals(manifest.ProjectId, identity.ProjectId, StringComparison.Ordinal))
            throw new InvalidDataException("Debug content belongs to a different project.");
        if (!string.Equals(manifest.ContentFingerprint, request.ContentFingerprint, StringComparison.Ordinal) ||
            !string.Equals(manifest.ContractFingerprint, request.ContractFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("Debug content manifest does not match the install request.");
        if (!string.Equals(identity.ContractFingerprint, request.ContractFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("Project contracts changed; restart the debug application.");
    }

    public async ValueTask DisposeAsync()
    {
        await _install.WaitAsync().ConfigureAwait(false);
        Candidate[] candidates;
        try
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                candidates = _owned.ToArray();
                _owned.Clear();
                _latest = null;
                _run = null;
            }
            foreach (var candidate in candidates) candidate.Resources.Dispose();
        }
        finally { _install.Release(); }
    }
}
