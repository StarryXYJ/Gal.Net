using GalNet.Core.Entry;

namespace GalNet.Editor.Shared.Services;

/// <summary>Initialized once by project opening, owned and released by its DI scope.</summary>
public sealed class ProjectSchemaSession : IDisposable
{
    private ProjectSchemaContext? _context;
    private bool _disposed;
    public IEntryCatalog Catalog => Context.Catalog;
    public ProjectSchemaContext Context => _context ?? throw new InvalidOperationException("Project schema session is not open.");

    public void Open(string projectDirectory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_context is not null) throw new InvalidOperationException("A project schema session can only be opened once.");
        _context = ProjectSchemaContext.Load(projectDirectory);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _context?.Dispose();
        _context = null;
    }
}
