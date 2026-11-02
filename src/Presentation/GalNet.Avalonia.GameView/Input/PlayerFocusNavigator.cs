using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using GalNet.Game.Controls;
using GalNet.Game.Controls.Forms;
using GalNet.Avalonia.GameView.Page;

namespace GalNet.Avalonia.GameView.Input;

/// <summary>Focus and editor interaction within one active page, toolbar or modal.</summary>
internal sealed class PlayerFocusNavigator(PlayerInputSettings settings)
{
    private sealed class FocusMemory { public WeakReference<Control>? Target; }
    private readonly ConditionalWeakTable<Control, FocusMemory> _memory = new();
    private Control? _scope;
    private ComboBox? _editingChoice;
    private int _originalChoice;

    public bool IsActivating { get; private set; }
    public Control? Focused => _scope is null ? null : TopLevel.GetTopLevel(_scope)?.FocusManager?.GetFocusedElement() as Control;

    public void SetScope(Control? scope)
    {
        if (ReferenceEquals(scope, _scope)) return;
        RememberFocus();
        CancelEdit();
        _scope = scope;
        if (scope is not null) EnsureFocus();
    }

    public void RememberFocus()
    {
        if (_scope is { } scope && Focused is { } focused && Contains(scope, focused))
            _memory.GetOrCreateValue(scope).Target = new(Target(focused));
    }

    public void EnsureFocus()
    {
        if (_scope is not { } scope) return;
        if (scope is ChoiceList choice)
        {
            if (choice.SelectedIndex < 0 && choice.ItemCount > 0) choice.PreviewSelection(0);
            Focus(choice);
            return;
        }
        var candidates = Candidates(scope);
        if (Focused is { } focused && candidates.Contains(Target(focused))) return;
        Control? remembered = null;
        _memory.TryGetValue(scope, out var memory);
        memory?.Target?.TryGetTarget(out remembered);
        Focus(remembered is not null && candidates.Contains(remembered) ? remembered :
            candidates.FirstOrDefault(PlayerNavigation.GetIsDefault) ?? candidates.FirstOrDefault());
    }

    public bool CancelEdit()
    {
        if (_editingChoice is null) return false;
        CloseChoice(false);
        return true;
    }

    public void Navigate(string code, bool confirm)
    {
        if (_scope is not { } scope) return;
        if (_editingChoice is { } editing)
        {
            if (confirm) CloseChoice(true);
            else if (code is "Up" or "Down" or "Left" or "Right")
                editing.SelectedIndex = Wrap(editing.SelectedIndex + (code is "Up" or "Left" ? -1 : 1), editing.ItemCount);
            return;
        }
        if (scope is ChoiceList choice)
        {
            if (confirm) { if (choice.SelectedIndex < 0) EnsureFocus(); else choice.CommitSelection(); }
            else if (code is "Up" or "Down" or "Left" or "Right")
            {
                choice.PreviewSelection(choice.SelectedIndex < 0 ? 0 : Wrap(choice.SelectedIndex + (code is "Up" or "Left" ? -1 : 1), choice.ItemCount));
                Focus(choice);
            }
            return;
        }
        if (code is "LT" or "RT" || scope is DialogueHistoryOverlay && Candidates(scope).Length <= 1 && code is "Up" or "Down")
        {
            var scroll = scope.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(control => control.IsEffectivelyVisible && control.Extent.Height > control.Viewport.Height);
            if (scroll is not null)
            {
                var amount = code is "LT" or "RT" ? scroll.Viewport.Height * .8 : 96;
                scroll.Offset = new(scroll.Offset.X, Math.Clamp(scroll.Offset.Y + (code is "LT" or "Up" ? -amount : amount), 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
                return;
            }
        }
        var candidates = Candidates(scope);
        var focused = Focused is { } current ? Target(current) : null;
        if (focused is null || !candidates.Contains(focused)) { EnsureFocus(); return; }
        if (code is "LB" or "RB")
        {
            var tabs = focused.GetSelfAndVisualAncestors().OfType<TabControl>().FirstOrDefault(control => Contains(scope, control))
                ?? SelfAndDescendants(scope).OfType<TabControl>().FirstOrDefault(control => control.IsEffectivelyVisible && control.IsEffectivelyEnabled);
            if (tabs is not null) SwitchTab(tabs, code == "LB" ? -1 : 1);
            return;
        }
        if (confirm)
        {
            switch (focused)
            {
                case InputBindingCapture capture: capture.BeginCapture(); break;
                case ComboBox combo:
                    _editingChoice = combo;
                    _originalChoice = combo.SelectedIndex;
                    settings.IsPreviewingChoice = true;
                    DefaultFieldEditors.SetIsSelectionPreview(combo, true);
                    combo.DropDownClosed += ChoiceClosed;
                    combo.AddHandler(InputElement.KeyDownEvent, ChoiceKeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
                    combo.IsDropDownOpen = true;
                    break;
                case Button button: SendKey(button, Key.Space); break;
                case TabItem tab: SendKey(tab, Key.Enter); break;
                case TextBox text: Focus(text); break;
            }
            return;
        }
        if (code is not ("Up" or "Down" or "Left" or "Right")) return;
        if (code is "Left" or "Right")
        {
            var increase = code == "Right";
            if (focused is Slider slider)
            {
                slider.SetCurrentValue(RangeBase.ValueProperty, Math.Clamp(slider.Value + (increase ? slider.SmallChange : -slider.SmallChange), slider.Minimum, slider.Maximum));
                return;
            }
            if (focused is NumericUpDown numeric) { SendKey(numeric, increase ? Key.Up : Key.Down); return; }
            if (focused is TabItem tab && tab.GetVisualAncestors().OfType<TabControl>().FirstOrDefault() is { } tabs)
            { SwitchTab(tabs, increase ? 1 : -1); return; }
        }
        Focus(FindNext(scope, focused, candidates, code));
    }

    private void CloseChoice(bool commit)
    {
        if (_editingChoice is not { } combo) return;
        _editingChoice = null;
        combo.DropDownClosed -= ChoiceClosed;
        combo.RemoveHandler(InputElement.KeyDownEvent, ChoiceKeyDown);
        var selected = combo.SelectedIndex;
        combo.SelectedIndex = _originalChoice;
        combo.IsDropDownOpen = false;
        settings.IsPreviewingChoice = false;
        DefaultFieldEditors.SetIsSelectionPreview(combo, false);
        if (commit) combo.SelectedIndex = selected;
        Focus(combo);
    }

    private void ChoiceClosed(object? sender, EventArgs e) => CloseChoice(true);
    private void ChoiceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CloseChoice(false);
        e.Handled = true;
    }

    public void SendKey(Control control, Key key)
    {
        // Use the native activation path: commands, toggle state, Click handlers and flyouts run once.
        IsActivating = true;
        try
        {
            control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = control });
            control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, Source = control });
        }
        finally { IsActivating = false; }
    }

    private static void SwitchTab(TabControl tabs, int delta)
    {
        if (tabs.ItemCount == 0) return;
        for (var step = 1; step <= tabs.ItemCount; step++)
        {
            var index = Wrap(tabs.SelectedIndex + step * delta, tabs.ItemCount);
            if (tabs.ContainerFromIndex(index) is Control { IsEffectivelyEnabled: false } or Control { IsEffectivelyVisible: false }) continue;
            tabs.SelectedIndex = index;
            if (tabs.ContainerFromIndex(index) is Control target) Focus(target);
            return;
        }
    }

    private static Control[] Candidates(Control scope) => SelfAndDescendants(scope)
        .Where(control => (control.Focusable || control is NumericUpDown) && control.IsEffectivelyEnabled && control.IsEffectivelyVisible &&
            control.Bounds.Width > 0 && control.Bounds.Height > 0 && PlayerNavigation.GetIsTarget(control) &&
            control is Button or ComboBox or Slider or TabItem or NumericUpDown or TextBox)
        .Where(control => !control.GetVisualAncestors().TakeWhile(parent => !ReferenceEquals(parent, scope))
            .Any(parent => parent is ComboBox or NumericUpDown or Slider or TextBox))
        .ToArray();

    private static Control Target(Control control) => control.GetSelfAndVisualAncestors().OfType<Control>()
        .FirstOrDefault(parent => parent is NumericUpDown or ComboBox or Slider) ?? control;

    private static bool Contains(Control scope, Control control) => ReferenceEquals(scope, control) || scope.IsVisualAncestorOf(control);
    private static IEnumerable<Control> SelfAndDescendants(Control scope) => new[] { scope }.Concat(scope.GetVisualDescendants().OfType<Control>());
    private static int Wrap(int value, int count) => count == 0 ? -1 : (value % count + count) % count;

    private static void Focus(Control? target)
    {
        if (target is NumericUpDown numeric && !numeric.Focusable)
        {
            if (numeric.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus(NavigationMethod.Directional) == true)
                numeric.BringIntoView();
        }
        else if (target?.Focus(NavigationMethod.Directional) == true) target.BringIntoView();
    }

    private static Control FindNext(Control scope, Control focused, Control[] candidates, string direction)
    {
        var origin = BoundsInScope(focused, scope);
        if (origin is null) return candidates[0];
        var vertical = direction is "Up" or "Down";
        var sign = direction is "Up" or "Left" ? -1 : 1;
        var center = origin.Value.Center;
        var targets = candidates.Where(control => !ReferenceEquals(control, focused))
            .Select(control => (Control: control, Bounds: BoundsInScope(control, scope)))
            .Where(item => item.Bounds is not null)
            .Select(item => (item.Control, Bounds: item.Bounds!.Value)).ToArray();
        double Along(Rect rect) => vertical ? rect.Center.Y : rect.Center.X;
        double Across(Rect rect) => vertical ? Math.Abs(rect.Center.X - center.X) : Math.Abs(rect.Center.Y - center.Y);
        bool Aligned(Rect rect) => vertical ? rect.Right > origin.Value.Left + 1 && rect.Left < origin.Value.Right - 1 :
            rect.Bottom > origin.Value.Top + 1 && rect.Top < origin.Value.Bottom - 1;
        var ahead = targets.Where(item => (Along(item.Bounds) - (vertical ? center.Y : center.X)) * sign > 1).ToArray();
        // Prefer the same row/column; geometry is recalculated after every layout change.
        var next = ahead.OrderBy(item => Aligned(item.Bounds) ? 0 : 1)
            .ThenBy(item => Math.Abs(Along(item.Bounds) - (vertical ? center.Y : center.X)) + Across(item.Bounds) * 2)
            .FirstOrDefault().Control;
        if (next is not null) return next;
        return targets.OrderBy(item => Aligned(item.Bounds) ? 0 : 1)
            .ThenBy(item => Along(item.Bounds) * sign).ThenBy(item => Across(item.Bounds)).FirstOrDefault().Control ?? focused;
    }

    private static Rect? BoundsInScope(Control control, Control scope)
    {
        var transform = control.TransformToVisual(scope);
        return transform is { } matrix ? new Rect(control.Bounds.Size).TransformToAABB(matrix) : null;
    }
}
