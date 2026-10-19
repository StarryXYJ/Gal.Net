using System.ComponentModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace GalNet.Game.Controls.Forms;

/// <summary>Explicit author presence actions. Hosts can localize the tooltip properties with styles.</summary>
public sealed class FieldEditorActions : StackPanel
{
    public static readonly StyledProperty<bool> SimplifiedProperty = AvaloniaProperty.Register<FieldEditorActions, bool>(nameof(Simplified));
    public bool Simplified { get => GetValue(SimplifiedProperty); set => SetValue(SimplifiedProperty, value); }
    public static readonly StyledProperty<string> UnavailableTextProperty = AvaloniaProperty.Register<FieldEditorActions, string>(nameof(UnavailableText), "This required field has no default value.");
    public string UnavailableText { get => GetValue(UnavailableTextProperty); set => SetValue(UnavailableTextProperty, value); }
    public static readonly StyledProperty<FieldEditorContext?> ContextProperty = AvaloniaProperty.Register<FieldEditorActions, FieldEditorContext?>(nameof(Context));
    public static readonly StyledProperty<string> DefaultTextProperty = AvaloniaProperty.Register<FieldEditorActions, string>(nameof(DefaultText), "Use default value");
    public static readonly StyledProperty<string> OmitTextProperty = AvaloniaProperty.Register<FieldEditorActions, string>(nameof(OmitText), "Omit this field");
    public static readonly StyledProperty<string> NullTextProperty = AvaloniaProperty.Register<FieldEditorActions, string>(nameof(NullText), "Set explicit null");
    public static readonly StyledProperty<string> RevertTextProperty = AvaloniaProperty.Register<FieldEditorActions, string>(nameof(RevertText), "Revert uncommitted input");
    public FieldEditorContext? Context { get => GetValue(ContextProperty); set => SetValue(ContextProperty, value); }
    public string DefaultText { get => GetValue(DefaultTextProperty); set => SetValue(DefaultTextProperty, value); }
    public string OmitText { get => GetValue(OmitTextProperty); set => SetValue(OmitTextProperty, value); }
    public string NullText { get => GetValue(NullTextProperty); set => SetValue(NullTextProperty, value); }
    public string RevertText { get => GetValue(RevertTextProperty); set => SetValue(RevertTextProperty, value); }
    private readonly Button _reset = new() { Name = "UseDefault", Content = "↺" };
    private readonly Button _omit = new() { Name = "OmitField", Content = "∅" };
    private readonly Button _null = new() { Name = "SetNull", Content = "null" };
    private readonly Button _revert = new() { Name = "RevertDraft", Content = "↶" };
    private readonly TextBlock _state = new() { VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
    private FieldEditorContext? _subscribed;
    private bool _mounted;
    public FieldEditorActions()
    {
        Orientation = global::Avalonia.Layout.Orientation.Horizontal; Spacing = 4;
        Children.Add(_state); Children.Add(_reset); Children.Add(_omit); Children.Add(_null); Children.Add(_revert);
        _reset.Click += (_, _) => { if (Context is { } context) _ = Simplified ? context.ResetAsync() : context.ResetDefaultAsync(); };
        _omit.Click += (_, _) => { if (Context is { } context) _ = context.OmitAsync(); };
        _null.Click += (_, _) => { if (Context is { } context) _ = context.SetNullAsync(); };
        _revert.Click += (_, _) => Context?.RevertDraft();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _mounted = true; Subscribe(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _mounted = false; Unsubscribe(); base.OnDetachedFromVisualTree(e); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == ContextProperty && _mounted) Subscribe(); Refresh(); }
    private void Subscribe() { Unsubscribe(); _subscribed = Context; if (_subscribed is not null) _subscribed.PropertyChanged += Changed; Refresh(); }
    private void Unsubscribe() { if (_subscribed is not null) _subscribed.PropertyChanged -= Changed; _subscribed = null; }
    private void Changed(object? sender, PropertyChangedEventArgs args) => Refresh();
    private void Refresh()
    {
        if (_reset is null) return;
        var context = Context;
        _reset.IsVisible = Simplified || context?.HasDefault == true;
        _reset.IsEnabled = context is not null && (Simplified ? context.CanReset : context.HasDefault);
        _omit.IsVisible = !Simplified && context?.CanOmit == true;
        _null.IsVisible = !Simplified && context?.CanSetNull == true; _revert.IsVisible = !Simplified && context?.HasDraft == true;
        _state.IsVisible = !Simplified;
        IsEnabled = context?.IsEnabled == true;
        _state.Text = context is null ? "" : !context.Value.IsProvided ? context.HasDefault ? "↳" : "∅" : context.Value.Json?.ValueKind == JsonValueKind.Null ? "null" : "";
        ToolTip.SetTip(_state, context is { Value.IsProvided: false } ? context.HasDefault ? DefaultText : OmitText : NullText);
        var resetText = Simplified && context is { CanReset: false } ? UnavailableText
            : Simplified && context?.HasDefault != true ? OmitText : DefaultText;
        ToolTip.SetTip(_reset, resetText);
        global::Avalonia.Automation.AutomationProperties.SetName(_reset, resetText);
        ToolTip.SetTip(_omit, OmitText); ToolTip.SetTip(_null, NullText); ToolTip.SetTip(_revert, RevertText);
    }
}
