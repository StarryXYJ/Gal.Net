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
    public static readonly StyledProperty<double> NarrowWidthProperty = AvaloniaProperty.Register<FieldEditorRow, double>(nameof(NarrowWidth), 480);
    public static readonly StyledProperty<double> LabelWidthProperty = AvaloniaProperty.Register<FieldEditorRow, double>(nameof(LabelWidth), 130);
    public static readonly StyledProperty<bool> ShowActionsProperty = AvaloniaProperty.Register<FieldEditorRow, bool>(nameof(ShowActions), true);
    public static readonly StyledProperty<bool> ShowLabelProperty = AvaloniaProperty.Register<FieldEditorRow, bool>(nameof(ShowLabel), true);
    public object? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public Control? Editor { get => GetValue(EditorProperty); set => SetValue(EditorProperty, value); }
    public FieldEditorContext? Context { get => GetValue(ContextProperty); set => SetValue(ContextProperty, value); }
    public FieldDescriptor? Field { get => GetValue(FieldProperty); set => SetValue(FieldProperty, value); }
    public IFieldBinding? Binding { get => GetValue(BindingProperty); set => SetValue(BindingProperty, value); }
    public double NarrowWidth { get => GetValue(NarrowWidthProperty); set => SetValue(NarrowWidthProperty, value); }
    public double LabelWidth { get => GetValue(LabelWidthProperty); set => SetValue(LabelWidthProperty, value); }
    public bool ShowActions { get => GetValue(ShowActionsProperty); set => SetValue(ShowActionsProperty, value); }
    public bool ShowLabel { get => GetValue(ShowLabelProperty); set => SetValue(ShowLabelProperty, value); }
    private readonly ContentControl _label = new() { Name = "FieldLabel", VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 6, 0, 0) };
    private readonly ContentControl _editor = new() { Name = "FieldEditor", HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top };
    private readonly FieldEditorActions _actions = new() { Simplified = true, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _issue = new() { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
    private FieldEditorContext? _owned, _active;
    private bool _mounted, _narrow;

    public FieldEditorRow()
    {
        ColumnSpacing = 8; RowSpacing = 4;
        Children.Add(_label); Children.Add(_editor); Children.Add(_actions); Children.Add(_issue);
        Layout(false);
        SizeChanged += (_, _) => { var narrow = Bounds.Width < NarrowWidth; if (_narrow != narrow) Layout(narrow); };
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); _mounted = true;
        if (Editor is ContentControl editor) editor.PropertyChanged += EditorContentChanged;
        Connect(); Layout(_narrow);
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _mounted = false;
        if (Editor is ContentControl editor) editor.PropertyChanged -= EditorContentChanged;
        Disconnect(); base.OnDetachedFromVisualTree(e);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_label is null) return;
        if (change.Property == LabelProperty) _label.Content = Label;
        if (change.Property == EditorProperty)
        {
            if (_mounted && change.OldValue is ContentControl previous) previous.PropertyChanged -= EditorContentChanged;
            if (_mounted && change.NewValue is ContentControl next) next.PropertyChanged += EditorContentChanged;
            _editor.Content = Editor; Layout(_narrow);
        }
        if (change.Property == ContextProperty || change.Property == FieldProperty || change.Property == BindingProperty) { if (_mounted) Connect(); }
        if (change.Property == NarrowWidthProperty) Layout(Bounds.Width < NarrowWidth);
        if (change.Property == ShowLabelProperty || change.Property == LabelWidthProperty || change.Property == ShowActionsProperty) Layout(_narrow);
    }
    private void EditorContentChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == ContentControl.ContentProperty) Layout(_narrow);
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
        _issue.Text = _active?.IssueMessage; _issue.IsVisible = !string.IsNullOrEmpty(_issue.Text);
        IsVisible = _active?.IsVisible ?? true;
        _editor.IsEnabled = _active?.IsEnabled ?? true;
    }
    private void Layout(bool narrow)
    {
        _narrow = narrow;
        var showLabel = ShowLabel && Editor is not StructuredFieldEditor && Editor is not ContentControl { Content: StructuredFieldEditor };
        _label.IsVisible = showLabel;
        _actions.IsVisible = ShowActions;
        ColumnDefinitions = narrow || !showLabel ? new("*,Auto") : new ColumnDefinitions { new(LabelWidth, GridUnitType.Pixel), new(1, GridUnitType.Star), new(GridLength.Auto) };
        RowDefinitions = new(narrow && showLabel ? "Auto,Auto,Auto" : "Auto,Auto");
        SetRow(_label, 0); SetColumn(_label, 0);
        SetRow(_editor, narrow && showLabel ? 1 : 0); SetColumn(_editor, narrow || !showLabel ? 0 : 1);
        SetRow(_actions, 0); SetColumn(_actions, narrow || !showLabel ? 1 : 2);
        SetRow(_issue, narrow && showLabel ? 2 : 1); SetColumn(_issue, narrow || !showLabel ? 0 : 1);
        _label.Margin = new(0, narrow ? 0 : 6, 0, 0);
    }
}
