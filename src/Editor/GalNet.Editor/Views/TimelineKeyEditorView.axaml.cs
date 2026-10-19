using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GalNet.Editor.Services;

namespace GalNet.Editor.Views;

public partial class TimelineKeyEditorView : UserControl, IDisposable
{
    private readonly List<IDisposable> _bindings = [];
    public TimelineKeyEditorView() { InitializeComponent(); AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel); }
    public void Configure(TimelineEditSession session)
    {
        _bindings.Add(Bind(DataContextProperty, new Binding(nameof(session.KeyEdit)) { Source = session }));
        _bindings.Add(this.FindControl<CheckBox>("TimelineKeySeconds")!.Bind(Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding(nameof(session.TangentsPerSecond)) { Source = session, Mode = BindingMode.TwoWay }));
    }
    private void OnInputKeyDown(object? sender, KeyEventArgs args)
    {
        var detail = args.Source as NumericUpDown ?? (args.Source as Control)?.FindAncestorOfType<NumericUpDown>();
        if (detail?.DataContext is TimelineKeyEdit key)
        {
            if (args.Key == Key.Enter) { CommitKeyDetail(detail, key); args.Handled = true; }
            else if (args.Key == Key.Escape) { key.Cancel(); args.Handled = true; }
            return;
        }
    }
    private static void CommitKeyDetail(NumericUpDown input, TimelineKeyEdit key)
    {
        if (input.Name == "TimelineKeyValue") key.CommitValue();
        else if (input.Name == "TimelineKeyIncoming") key.CommitIncoming();
        else if (input.Name == "TimelineKeyOutgoing") key.CommitOutgoing();
    }
    private void OnKeyDetailLostFocus(object? sender, FocusChangedEventArgs args)
    { if (sender is NumericUpDown { DataContext: TimelineKeyEdit key } input) CommitKeyDetail(input, key); }
    private void OnKeyInterpolationChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (sender is not ComboBox { DataContext: TimelineKeyEdit key } input || input.SelectedIndex < 0) return;
        key.InterpolationIndex = input.SelectedIndex; key.CommitInterpolation();
        input.SetCurrentValue(Avalonia.Controls.Primitives.SelectingItemsControl.SelectedIndexProperty, key.InterpolationIndex);
    }
    public void Dispose() { foreach (var binding in _bindings) binding.Dispose(); _bindings.Clear(); }
}
