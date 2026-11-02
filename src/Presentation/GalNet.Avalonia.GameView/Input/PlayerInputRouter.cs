using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;

namespace GalNet.Avalonia.GameView.Input;

/// <summary>Routes gameplay gestures and navigation exclusively to the current interaction surface.</summary>
public sealed class PlayerInputRouter : IDisposable
{
    private readonly Control _root;
    private readonly PlayerInputSettings _settings;
    private readonly Func<GamePresentationState?> _activeGame;
    private readonly Func<Control?> _activePage;
    private readonly GameActionDispatcher _actions;
    private readonly KeyboardMouseShortcutListener _keyboardMouse;
    private readonly PlayerFocusNavigator _focus;
    private readonly Action _back;
    private string? _gamepadDraft;
    private int _gamepadPeak;
    private readonly DispatcherTimer _navigationRepeat = new();
    private string? _navigationDirection;
    private Control? _repeatScope;
    private bool _toolbarActive;
    private Control? _externalScope;
    private Action? _externalCancel;

    public PlayerInputRouter(Control root, PlayerInputSettings settings, Func<GamePresentationState?> activeGame,
        IGameSessionService session, Action back, Func<string, Task>? applicationAction = null,
        Func<Control?>? activePage = null)
    {
        _root = root; _settings = settings; _activeGame = activeGame; _back = back;
        _activePage = activePage ?? (() => root);
        _focus = new(settings);
        _actions = new GameActionDispatcher(settings, activeGame, session, applicationAction);
        _keyboardMouse = new(root, settings, () => _actions.CanReceive && !IsNavigationActive && !_focus.IsActivating,
            _actions.DispatchAsync, _actions.ReleaseHold);
        settings.GamepadChanged += Gamepad;
        settings.GamepadCancelled += ResetInput;
        _navigationRepeat.Tick += NavigationRepeat;
        root.DetachedFromVisualTree += Detached;
        root.AddHandler(InputElement.PointerPressedEvent, PointerPressed, RoutingStrategies.Tunnel);
    }

    /// <summary>A host modal outside the shell can temporarily own the same input stream.</summary>
    public void SetNavigationScope(Control? scope, Action? cancel = null)
    {
        if (scope is not null) _settings.CancelGamepad();
        else ResetInput();
        _externalScope = scope;
        _externalCancel = cancel;
        if (scope is not null) _focus.SetScope(scope);
    }

    private bool IsNavigationActive => _externalScope is not null || _toolbarActive || OpenFlyoutOwner() is not null ||
        _activeGame() is null or { IsChoiceVisible: true } or { IsSkipConfirmationOpen: true } or { IsHistoryOpen: true };

    private Control? NavigationScope()
    {
        if (_externalScope is not null) return _externalScope;
        if (_activeGame() is { } game)
        {
            if (game.IsSkipConfirmationOpen) return _root.GetVisualDescendants().OfType<SkipConfirmation>().FirstOrDefault();
            var page = _activePage() as GamePage ?? _activePage()?.GetVisualDescendants().OfType<GamePage>().FirstOrDefault();
            if (game.IsHistoryOpen) return page?.History;
            if (game.IsChoiceVisible) return page?.Choice;
            if (_toolbarActive) return Toolbar();
            return null;
        }
        return _activePage();
    }

    private Control? Toolbar()
    {
        var page = _activePage();
        return page?.GetVisualDescendants().OfType<Control>().Prepend(page)
            .FirstOrDefault(control => PlayerNavigation.GetIsToolbar(control) && control.IsEffectivelyVisible && control.IsEffectivelyEnabled);
    }

    private bool CanReceive => (_externalScope ?? _root) is { IsEffectivelyEnabled: true, IsEffectivelyVisible: true };

    private async void Gamepad(IReadOnlySet<string> buttons)
    {
        if (!CanReceive) { ResetInput(); return; }
        if (_settings.IsCapturing) { ClearGesture(); return; }
        if (_toolbarActive && Toolbar() is null) LeaveToolbar();
        if (IsNavigationActive && buttons.Count > 0 && buttons.All(code => code is "Up" or "Down" or "Left" or "Right"))
        {
            var direction = _navigationDirection is { } previous && buttons.Contains(previous) ? previous :
                new[] { "Up", "Down", "Left", "Right" }.First(buttons.Contains);
            _gamepadDraft = null; _gamepadPeak = 0;
            if (_navigationDirection != direction)
            {
                StopNavigationRepeat();
                Navigate(direction);
                _repeatScope = NavigationScope();
                _navigationDirection = direction;
                _navigationRepeat.Interval = TimeSpan.FromMilliseconds(400);
                _navigationRepeat.Start();
            }
            return;
        }
        StopNavigationRepeat();
        if (buttons.Count > 0)
        {
            if (buttons.Count >= _gamepadPeak)
            { _gamepadPeak = buttons.Count; _gamepadDraft = string.Join('+', buttons.Order(StringComparer.Ordinal)); }
            if (!IsNavigationActive && _gamepadDraft is { } draft && _settings.Resolve(PlayerInputDevice.Gamepad, draft) == "hold.quick")
                await DispatchAsync("hold.quick");
            else ReleaseHold();
            return;
        }
        ReleaseHold();
        var gesture = _gamepadDraft;
        _gamepadDraft = null; _gamepadPeak = 0;
        if (gesture is null) return;
        if (gesture == "Start" && _activeGame() is { IsChoiceVisible: false, IsHistoryOpen: false, IsSkipConfirmationOpen: false } game && _externalScope is null)
        {
            if (OpenFlyoutOwner() is { } owner) owner.Flyout!.Hide();
            if (_toolbarActive) LeaveToolbar();
            else
            {
                game.IsUiHidden = false;
                game.CancelPlayback();
                _toolbarActive = Toolbar() is not null;
                _focus.SetScope(NavigationScope());
            }
            return;
        }
        if (IsNavigationActive) { Navigate(gesture); return; }
        var action = _settings.Resolve(PlayerInputDevice.Gamepad, gesture);
        if (action != "hold.quick" && action is not null) await DispatchAsync(action);
    }

    private void Navigate(string code)
    {
        var cancel = code == (_settings.EastConfirms ? "South" : "East");
        if (OpenFlyoutOwner() is { } owner)
        {
            if (cancel) { owner.Flyout!.Hide(); owner.Focus(NavigationMethod.Directional); }
            else NavigateFlyout(owner, code);
            return;
        }
        var scope = NavigationScope();
        if (scope is null || !scope.IsEffectivelyEnabled || !scope.IsEffectivelyVisible) return;
        _focus.SetScope(scope);
        if (cancel)
        {
            if (_focus.CancelEdit()) return;
            if (_externalScope is not null) _externalCancel?.Invoke();
            else if (_activeGame() is { IsSkipConfirmationOpen: true } prompt) prompt.CancelSkipCommand.Execute(null);
            else if (_activeGame() is { IsHistoryOpen: true } history) history.CloseHistoryCommand.Execute(null);
            else if (_activeGame() is { IsChoiceVisible: true }) { } // A story branch cannot be cancelled into gameplay.
            else if (_toolbarActive) LeaveToolbar();
            else _back();
            return;
        }
        _focus.Navigate(code, code == (_settings.EastConfirms ? "East" : "South"));
        _focus.RememberFocus();
    }

    private void NavigateFlyout(Button owner, string code)
    {
        var focused = TopLevel.GetTopLevel(_root)?.FocusManager?.GetFocusedElement() as Control;
        if (focused is null || !focused.GetSelfAndVisualAncestors().Any(control => control is MenuFlyoutPresenter or FlyoutPresenter))
        {
            var first = (owner.Flyout as MenuFlyout)?.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsEffectivelyVisible && item.IsEffectivelyEnabled);
            if (first?.Focus(NavigationMethod.Directional) != true) return;
            focused = first;
            if (code is "Up" or "Down" or "Left" or "Right") return;
        }
        var key = code switch
        {
            "Up" => Key.Up, "Down" => Key.Down, "Left" => Key.Left, "Right" => Key.Right,
            _ when code == (_settings.EastConfirms ? "East" : "South") => Key.Enter,
            _ => Key.None
        };
        if (key != Key.None) _focus.SendKey(focused, key);
    }

    private Button? OpenFlyoutOwner()
    {
        var page = _externalScope ?? _activePage() ?? _root;
        return page.GetVisualDescendants().Prepend(page).OfType<Button>()
            .FirstOrDefault(button => button.IsEffectivelyVisible && button.IsEffectivelyEnabled && button.Flyout?.IsOpen == true);
    }

    private void LeaveToolbar()
    {
        _toolbarActive = false;
        _focus.SetScope(null);
        // Give story input back without leaving a misleading focused toolbar action.
        _root.Focusable = true;
        _root.Focus();
    }

    private void NavigationRepeat(object? sender, EventArgs e)
    {
        _navigationRepeat.Interval = TimeSpan.FromMilliseconds(100);
        if (!CanReceive || !IsNavigationActive || _settings.IsCapturing || !ReferenceEquals(_repeatScope, NavigationScope()))
        { StopNavigationRepeat(); return; }
        if (_navigationDirection is { } direction) Navigate(direction);
    }

    private void StopNavigationRepeat() { _navigationDirection = null; _repeatScope = null; _navigationRepeat.Stop(); }
    private void ClearGesture() { _gamepadDraft = null; _gamepadPeak = 0; StopNavigationRepeat(); }
    public Task DispatchAsync(string action) => !CanReceive || IsNavigationActive ? Task.CompletedTask : _actions.DispatchAsync(action);
    public void ReleaseHold() => _actions.ReleaseHold();
    public void ResetInput()
    {
        _keyboardMouse.Reset();
        ClearGesture();
        ReleaseHold();
        _focus.SetScope(null);
        _focus.CancelEdit();
        _toolbarActive = false;
    }
    /// <summary>Called after page/overlay layout changes; mouse users keep their focus.</summary>
    public void RefreshNavigation()
    {
        if (_settings.LastDevice != PlayerInputDevice.Gamepad || _settings.IsCapturing || !CanReceive) return;
        _focus.SetScope(IsNavigationActive ? NavigationScope() : null);
        _focus.EnsureFocus();
    }

    private void PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ClearGesture();
        ReleaseHold();
        _toolbarActive = false;
        _focus.SetScope(null);
    }
    private void Detached(object? sender, VisualTreeAttachmentEventArgs e) => ResetInput();
    public void Dispose()
    {
        _settings.GamepadChanged -= Gamepad;
        _settings.GamepadCancelled -= ResetInput;
        ResetInput();
        _navigationRepeat.Tick -= NavigationRepeat;
        _keyboardMouse.Dispose();
        _root.DetachedFromVisualTree -= Detached;
        _root.RemoveHandler(InputElement.PointerPressedEvent, PointerPressed);
        _externalScope = null; _externalCancel = null;
    }
}
