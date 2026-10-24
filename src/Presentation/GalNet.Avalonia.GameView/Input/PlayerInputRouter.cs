using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;

namespace GalNet.Avalonia.GameView.Input;

/// <summary>Only the active gameplay page receives story actions; the shell tracks input mode on every page.</summary>
public sealed class PlayerInputRouter : IDisposable
{
    private readonly Control _root;
    private readonly PlayerInputSettings _settings;
    private readonly Func<GamePageViewModel?> _activeGame;
    private readonly GameActionDispatcher _actions;
    private readonly KeyboardMouseShortcutListener _keyboardMouse;
    private readonly Action _back;
    private string? _gamepadDraft;
    private int _gamepadPeak;
    private readonly DispatcherTimer _navigationRepeat = new();
    private string? _navigationDirection;
    private ComboBox? _editingChoice;
    private int _originalChoice;
    public PlayerInputRouter(Control root, PlayerInputSettings settings, Func<GamePageViewModel?> activeGame, IGameSessionService session, Action back)
    {
        _root = root; _settings = settings; _activeGame = activeGame; _back = back;
        _actions = new GameActionDispatcher(settings, activeGame, session);
        _keyboardMouse = new KeyboardMouseShortcutListener(root, settings, () => _actions.CanReceive,
            _actions.DispatchAsync, _actions.ReleaseHold);
        settings.GamepadChanged += Gamepad;
        settings.GamepadCancelled += ResetInput;
        _navigationRepeat.Tick += NavigationRepeat;
        root.DetachedFromVisualTree += Detached;
    }
    private async void Gamepad(IReadOnlySet<string> buttons)
    {
        if (_settings.IsCapturing) { _gamepadDraft = null; _gamepadPeak = 0; StopNavigationRepeat(); return; }
        var navigation = _activeGame() is null or { IsChoiceVisible: true } or { IsSkipConfirmationOpen: true };
        if (navigation && buttons.Count == 1 && buttons.First() is "Up" or "Down" or "Left" or "Right")
        {
            var direction = buttons.First();
            _gamepadDraft = null;
            if (_navigationDirection != direction)
            {
                StopNavigationRepeat();
                _navigationDirection = direction;
                Navigate(direction);
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
            if (_activeGame() is { IsChoiceVisible: false } && _gamepadDraft is { } draft && _settings.Resolve(PlayerInputDevice.Gamepad, draft) == "hold.quick")
                await DispatchAsync("hold.quick");
            else ReleaseHold();
            return;
        }
        ReleaseHold();
        var gesture = _gamepadDraft;
        _gamepadDraft = null;
        _gamepadPeak = 0;
        if (gesture is null) return;
        var game = _activeGame();
        if (game is null || game.IsChoiceVisible || game.IsSkipConfirmationOpen)
        { Navigate(gesture); return; }
        var action = _settings.Resolve(PlayerInputDevice.Gamepad, gesture);
        if (action != "hold.quick" && action is not null) await DispatchAsync(action);
    }
    private void StopNavigationRepeat() { _navigationDirection = null; _navigationRepeat.Stop(); }
    private void NavigationRepeat(object? sender, EventArgs e)
    {
        _navigationRepeat.Interval = TimeSpan.FromMilliseconds(100);
        if (_navigationDirection is { } direction && !_settings.IsCapturing) Navigate(direction);
    }
    private void CloseChoice(bool commit)
    {
        if (_editingChoice is not { } combo) return;
        var selected = combo.SelectedIndex;
        combo.SelectedIndex = _originalChoice;
        combo.IsDropDownOpen = false;
        _editingChoice = null;
        _settings.IsPreviewingChoice = false;
        if (commit) combo.SelectedIndex = selected;
    }
    private void Navigate(string code)
    {
        var focused = TopLevel.GetTopLevel(_root)?.FocusManager?.GetFocusedElement() as Control;
        if (code == (_settings.EastConfirms ? "South" : "East"))
        {
            if (_editingChoice is not null) CloseChoice(false);
            else if (focused is ComboBox { IsDropDownOpen: true } open) open.IsDropDownOpen = false;
            else if (_activeGame() is { IsSkipConfirmationOpen: true } prompt) prompt.CancelSkipCommand.Execute(null);
            else _back();
            return;
        }
        if (code is "LB" or "RB")
        {
            var tabs = _root.GetVisualDescendants().OfType<TabControl>().FirstOrDefault();
            if (tabs is not null) tabs.SelectedIndex = Math.Clamp(tabs.SelectedIndex + (code == "LB" ? -1 : 1), 0, tabs.ItemCount - 1);
            return;
        }
        var candidates = _root.GetVisualDescendants().OfType<Control>()
            .Where(control => control.IsEffectivelyVisible && control.IsEffectivelyEnabled && control.Focusable && control is Button or ComboBox or Slider or TabItem).ToArray();
        if (code == (_settings.EastConfirms ? "East" : "South"))
        {
            if (_editingChoice is not null) { CloseChoice(true); return; }
            switch (focused)
            {
                case InputBindingCapture capture: capture.BeginCapture(); break;
                case ToggleButton toggle: toggle.IsChecked = toggle.IsChecked != true; break;
                case ComboBox combo:
                    if (_editingChoice is not null) CloseChoice(true);
                    else { _editingChoice = combo; _originalChoice = combo.SelectedIndex; _settings.IsPreviewingChoice = true; combo.IsDropDownOpen = true; }
                    break;
                case Button button:
                    if (button.Command is { } command) { if (command.CanExecute(button.CommandParameter)) command.Execute(button.CommandParameter); }
                    else button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    break;
                default: candidates.FirstOrDefault()?.Focus(); break;
            }
            return;
        }
        if (code is not ("Up" or "Down" or "Left" or "Right")) return;
        if (_editingChoice is { } selection)
        { selection.SelectedIndex = Math.Clamp(selection.SelectedIndex + (code is "Up" or "Left" ? -1 : 1), 0, selection.ItemCount - 1); return; }
        if (focused is Slider slider && code is "Left" or "Right")
        { slider.Value = Math.Clamp(slider.Value + (code == "Left" ? -slider.SmallChange : slider.SmallChange), slider.Minimum, slider.Maximum); return; }
        if (focused is TabItem && code is "Left" or "Right")
        {
            var tabs = focused.GetVisualAncestors().OfType<TabControl>().FirstOrDefault();
            if (tabs is not null) tabs.SelectedIndex = Math.Clamp(tabs.SelectedIndex + (code == "Left" ? -1 : 1), 0, tabs.ItemCount - 1);
            return;
        }
        var origin = focused?.TranslatePoint(default, _root);
        Control? next = null;
        if (origin is { } point)
            next = candidates.Where(control => !ReferenceEquals(control, focused)).Select(control => (Control: control, Point: control.TranslatePoint(default, _root)))
                .Where(item => item.Point is { } target && (code switch { "Down" => target.Y > point.Y + 1, "Up" => target.Y < point.Y - 1, "Left" => target.X < point.X - 1, _ => target.X > point.X + 1 }))
                .OrderBy(item => item.Point is { } target ? (code is "Up" or "Down" ? Math.Abs(target.Y - point.Y) + Math.Abs(target.X - point.X) * 2 : Math.Abs(target.X - point.X) + Math.Abs(target.Y - point.Y) * 2) : double.MaxValue)
                .Select(item => item.Control).FirstOrDefault();
        next ??= candidates.FirstOrDefault();
        if (next?.Focus() == true) next.BringIntoView();
    }
    public Task DispatchAsync(string action) => _actions.DispatchAsync(action);
    public void ReleaseHold() => _actions.ReleaseHold();
    public void ResetInput()
    {
        _keyboardMouse.Reset();
        _gamepadDraft = null;
        _gamepadPeak = 0;
        ReleaseHold();
        StopNavigationRepeat();
        CloseChoice(false);
    }
    private void Detached(object? sender, VisualTreeAttachmentEventArgs e) => ResetInput();
    public void Dispose()
    {
        _settings.GamepadChanged -= Gamepad;
        _settings.GamepadCancelled -= ResetInput;
        StopNavigationRepeat();
        _navigationRepeat.Tick -= NavigationRepeat;
        CloseChoice(false);
        _keyboardMouse.Dispose();
        ReleaseHold();
        _root.DetachedFromVisualTree -= Detached;
    }
}
