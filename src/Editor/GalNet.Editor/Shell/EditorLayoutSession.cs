using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Core;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Dock;
using GalNet.Editor.Services;
using Serilog;

namespace GalNet.Editor.Shell;

/// <summary>Owns layout restoration, named layouts and debounced persistence for one project shell.</summary>
public sealed class EditorLayoutSession : IDisposable
{
    private readonly EditorDockFactory _dockFactory;
    private readonly DockLayoutSerializer _dockLayoutSerializer;
    private readonly IEditorSettingsService _editorSettingsService;
    private readonly DispatcherTimer _layoutSaveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _disposed;
    private bool _savingLayout;
    private bool _layoutSavePending;
    public IRootDock? Layout { get; private set; }
    public EditorText? Status { get; private set; }
    public IEnumerable<string> Names => _editorSettingsService.GetSettings().DockLayouts.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
    public event Action? Changed;
    public event Action? NamesChanged;
    public EditorLayoutSession(EditorDockFactory dockFactory, DockLayoutSerializer serializer, IEditorSettingsService settings)
    {
        _dockFactory = dockFactory; _dockLayoutSerializer = serializer; _editorSettingsService = settings;
        _layoutSaveTimer.Tick += OnLayoutSaveTimerTick;
        _dockFactory.LayoutChanged += OnDockLayoutChanged;
    }
    private void SetLayoutStatus(EditorText text) { Status = text; Changed?.Invoke(); }
    public void Initialize()
    {
        var settings = _editorSettingsService.GetSettings();
        Layout = TryLoadLayout(settings.LastDockLayout);
        if (Layout is null && !string.IsNullOrWhiteSpace(settings.LastDockLayout))
        {
            // A previous implementation could persist the host Window back-reference.
            // Discard that malformed global value once so every subsequent start is clean.
            settings.LastDockLayout = null;
            _editorSettingsService.SaveSettings();
            SetLayoutStatus(new("Status.LayoutReset"));
        }
        if (Layout is null)
        {
            Layout = _dockFactory.CreateLayout();
            _dockFactory.InitLayout(Layout);
        }
        Changed?.Invoke();
    }

    public async Task SaveAsync(Func<Task<string?>> requestName)
    {
        if (Layout is null)
            return;
        var name = await requestName();
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase))
        {
            SetLayoutStatus(new("Layout.Reserved"));
            return;
        }
        var settings = _editorSettingsService.GetSettings();
        settings.DockLayouts[name] = SerializeLayout(Layout);
        _editorSettingsService.SaveSettings();
        SetLayoutStatus(new("Layout.Saved", name));
        NamesChanged?.Invoke();
    }

    public void Load(string? name)
    {
        if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase))
        {
            Reset();
            return;
        }
        var settings = _editorSettingsService.GetSettings();
        if (string.IsNullOrWhiteSpace(name) || !settings.DockLayouts.TryGetValue(name, out var serialized))
        {
            SetLayoutStatus(new("Layout.None"));
            return;
        }
        var layout = TryLoadLayout(serialized);
        if (layout is null)
        {
            SetLayoutStatus(new("Layout.LoadFailed", name));
            return;
        }
        Layout = layout;
        Changed?.Invoke();
        SetLayoutStatus(new("Layout.Loaded", name));
    }

    public void Reset()
    {
        Layout = _dockFactory.CreateLayout();
        _dockFactory.InitLayout(Layout);
        Changed?.Invoke();
        SetLayoutStatus(new("Layout.Reset"));
    }

    public void Delete(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase)) return;
        var settings = _editorSettingsService.GetSettings();
        if (!settings.DockLayouts.Remove(name)) return;
        _editorSettingsService.SaveSettings();
        SetLayoutStatus(new("Layout.Deleted", name));
        NamesChanged?.Invoke();
    }

    private IRootDock? TryLoadLayout(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized)) return null;
        try
        {
            var layout = DockLayoutSerializer.Deserialize(serialized);
            if (layout is null)
                return null;

            _dockFactory.InitLayout(layout);
            return _dockFactory.PrepareRestoredLayout(layout) ? layout : null;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to restore editor dock layout");
            return null;
        }
    }

    private void OnDockLayoutChanged()
    {
        if (Layout is null) return;
        _layoutSavePending = true;
        _layoutSaveTimer.Stop();
        _layoutSaveTimer.Start();
        Changed?.Invoke();
    }

    private void OnLayoutSaveTimerTick(object? sender, EventArgs e)
    {
        _layoutSaveTimer.Stop();
        PersistLayoutNow();
    }

    private void PersistLayoutCore()
    {
        if (_savingLayout || Layout is null || _dockFactory.IsMaximized) return;
        try
        {
            _savingLayout = true;
            var serialized = SerializeLayout(Layout);
            _editorSettingsService.GetSettings().LastDockLayout = serialized;
            _editorSettingsService.SaveSettings();
        }
        catch (Exception exception)
        {
            SetLayoutStatus(new("Layout.SaveFailed"));
            Log.Error(exception, "Failed to persist editor dock layout");
        }
        finally { _savingLayout = false; }
    }

    /// <summary>Called after splitter interaction; persistence is debounced to avoid disk writes during dragging.</summary>
    public void PersistLayout() => OnDockLayoutChanged();

    /// <summary>Flushes a pending layout change. Called during application shutdown.</summary>
    public void PersistLayoutNow()
    {
        _layoutSaveTimer.Stop();
        if (!_layoutSavePending && Layout is null) return;
        _layoutSavePending = false;
        PersistLayoutCore();
    }

    private string SerializeLayout(IRootDock layout) => _dockLayoutSerializer.Serialize(layout);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PersistLayoutNow();
        _layoutSaveTimer.Tick -= OnLayoutSaveTimerTick;
        _dockFactory.LayoutChanged -= OnDockLayoutChanged;
    }
}
