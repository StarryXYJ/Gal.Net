using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using GalNet.Core.Parameters;

namespace GalNet.Game.Controls.Forms;

/// <summary>Responsive field chrome shared by generated and host-owned value controls.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Owned contexts are released on visual detachment and replaced on rebinding.")]
public sealed class FieldEditorRow : Grid
{
    public static readonly StyledProperty<object?> LabelProperty = AvaloniaProperty.Register<FieldEditorRow, object?>(nameof(Label));
    public static readonly StyledProperty<Control?> EditorProperty = AvaloniaProperty.Register<FieldEditorRow, Control?>(nameof(Editor));
    public static readonly StyledProperty<FieldEditorContext?> ContextProperty = AvaloniaProperty.Register<FieldEditorRow, FieldEditorContext?>(nameof(Context));
    public static readonly StyledProperty<FieldDescriptor?> FieldProperty = AvaloniaProperty.Register<FieldEditorRow, FieldDescriptor?>(nameof(Field));
    public static readonly StyledProperty<IFieldBinding?> BindingProperty = AvaloniaProperty.Register<FieldEditorRow, IFieldBinding?>(nameof(Binding));
    public static readonly StyledProperty<bool> ShowLabelProperty = AvaloniaProperty.Register<FieldEditorRow, bool>(nameof(ShowLabel), true);
    public object? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public Control? Editor { get => GetValue(EditorProperty); set => SetValue(EditorProperty, value); }
    public FieldEditorContext? Context { get => GetValue(ContextProperty); set => SetValue(ContextProperty, value); }
    public FieldDescriptor? Field { get => GetValue(FieldProperty); set => SetValue(FieldProperty, value); }
    public IFieldBinding? Binding { get => GetValue(BindingProperty); set => SetValue(BindingProperty, value); }
    public bool ShowLabel { get => GetValue(ShowLabelProperty); set => SetValue(ShowLabelProperty, value); }
    private readonly ContentControl _label = new() { Name = "FieldLabel", VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 6, 0, 0) };
    private readonly ContentControl _editor = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top };
    private readonly FieldEditorActions _actions = new() { Simplified = true, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _issue = new() { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
    private FieldEditorContext? _owned, _active;
    private bool _mounted, _narrow;

    public FieldEditorRow()
    {
        ColumnSpacing = 8; RowSpacing = 4;
        Children.Add(_label); Children.Add(_editor); Children.Add(_actions); Children.Add(_issue);
        Layout(false);
        SizeChanged += (_, _) => { var narrow = Bounds.Width < 480; if (_narrow != narrow) Layout(narrow); };
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _mounted = true; Connect(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _mounted = false; Disconnect(); base.OnDetachedFromVisualTree(e); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_label is null) return;
        if (change.Property == LabelProperty) _label.Content = Label;
        if (change.Property == EditorProperty) _editor.Content = Editor;
        if (change.Property == ContextProperty || change.Property == FieldProperty || change.Property == BindingProperty) { if (_mounted) Connect(); }
        if (change.Property == ShowLabelProperty) Layout(_narrow);
    }
    private void Connect()
    {
        Disconnect();
        _active = Context ?? (Field is { } field && Binding is { } binding ? _owned = new(field, binding) : null);
        if (_active is not null) _active.PropertyChanged += Changed;
        _actions.Context = _active; Refresh();
    }
    private void Disconnect()
    {
        if (_active is not null) _active.PropertyChanged -= Changed;
        _actions.Context = null; _active = null; _owned?.Dispose(); _owned = null;
    }
    private void Changed(object? sender, PropertyChangedEventArgs args) => Refresh();
    private void Refresh()
    {
        _issue.Text = _active?.Issue?.Message; _issue.IsVisible = !string.IsNullOrEmpty(_issue.Text);
        IsVisible = _active?.IsVisible ?? true;
        _editor.IsEnabled = _active?.IsEnabled ?? true;
    }
    private void Layout(bool narrow)
    {
        _narrow = narrow;
        _label.IsVisible = ShowLabel;
        ColumnDefinitions = new(narrow || !ShowLabel ? "*,Auto" : "130,*,Auto");
        RowDefinitions = new(narrow && ShowLabel ? "Auto,Auto,Auto" : "Auto,Auto");
        SetRow(_label, 0); SetColumn(_label, 0);
        SetRow(_editor, narrow && ShowLabel ? 1 : 0); SetColumn(_editor, narrow || !ShowLabel ? 0 : 1);
        SetRow(_actions, 0); SetColumn(_actions, narrow || !ShowLabel ? 1 : 2);
        SetRow(_issue, narrow && ShowLabel ? 2 : 1); SetColumn(_issue, narrow || !ShowLabel ? 0 : 1);
        _label.Margin = new(0, narrow ? 0 : 6, 0, 0);
    }
}
