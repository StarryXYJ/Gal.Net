using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameView.Navigation;

namespace GalNet.Avalonia.GameView.Input;

/// <summary>One shell-level Escape subscription; controls get first chance to cancel their own interaction.</summary>
public sealed class PageBackInputListener : IDisposable
{
    private readonly Control _root;
    private readonly Func<BackNavigablePageViewModel?> _currentPage;
    private readonly Func<bool> _isCapturing;
    private TopLevel? _topLevel;
    private KeyEventArgs? _pending;
    private bool _escapeDown;
    private bool _disposed;

    public PageBackInputListener(Control root, Func<BackNavigablePageViewModel?> currentPage, Func<bool>? isCapturing = null)
    {
        _root = root; _currentPage = currentPage; _isCapturing = isCapturing ?? (() => false);
        root.AttachedToVisualTree += Attached;
        root.DetachedFromVisualTree += Detached;
        Attach();
    }

    private void Attached(object? sender, VisualTreeAttachmentEventArgs e) => Attach();
    private void Detached(object? sender, VisualTreeAttachmentEventArgs e) => Detach();
    private void Attach()
    {
        var topLevel = TopLevel.GetTopLevel(_root);
        if (_disposed || ReferenceEquals(topLevel, _topLevel)) return;
        Detach();
        _topLevel = topLevel;
        if (topLevel is null) return;
        topLevel.AddHandler(InputElement.KeyDownEvent, TrackKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        topLevel.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Bubble);
        topLevel.AddHandler(InputElement.KeyUpEvent, KeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        if (topLevel is Window window) window.Deactivated += Deactivated;
    }

    private void TrackKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        _pending = _escapeDown ? null : e;
        _escapeDown = true;
    }

    private void KeyDown(object? sender, KeyEventArgs e)
    {
        if (!ReferenceEquals(_pending, e)) return;
        _pending = null;
        if (e.KeyModifiers != KeyModifiers.None || _isCapturing() ||
            !_root.IsEffectivelyVisible || !_root.IsEffectivelyEnabled) return;
        if (e.Source is not null && !ReferenceEquals(e.Source, _topLevel) &&
            (e.Source is not Visual visual || !visual.GetSelfAndVisualAncestors().Contains(_root))) return;
        if (_currentPage() is not { } page || !page.BackCommand.CanExecute(null)) return;
        e.Handled = true;
        page.BackCommand.Execute(null);
    }

    private void KeyUp(object? sender, KeyEventArgs e) { if (e.Key == Key.Escape) Reset(); }
    private void Deactivated(object? sender, EventArgs e) => Reset();
    private void Reset() { _pending = null; _escapeDown = false; }
    private void Detach()
    {
        Reset();
        if (_topLevel is not { } topLevel) return;
        topLevel.RemoveHandler(InputElement.KeyDownEvent, TrackKeyDown);
        topLevel.RemoveHandler(InputElement.KeyDownEvent, KeyDown);
        topLevel.RemoveHandler(InputElement.KeyUpEvent, KeyUp);
        if (topLevel is Window window) window.Deactivated -= Deactivated;
        _topLevel = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
        _root.AttachedToVisualTree -= Attached;
        _root.DetachedFromVisualTree -= Detached;
    }
}
