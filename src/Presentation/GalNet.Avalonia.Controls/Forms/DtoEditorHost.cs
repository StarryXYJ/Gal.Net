using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GalNet.Core.Parameters;

namespace GalNet.Game.Controls.Forms;

/// <summary>Dispatches one DTO to an override or the default form. Never owns the borrowed item/binding.</summary>
public sealed class DtoEditorHost : ContentControl
{
    public static readonly StyledProperty<FieldDisplayMode> DisplayModeProperty = AvaloniaProperty.Register<DtoEditorHost, FieldDisplayMode>(nameof(DisplayMode));
    public FieldDisplayMode DisplayMode { get => GetValue(DisplayModeProperty); set => SetValue(DisplayModeProperty, value); }
    public static readonly StyledProperty<int> DepthProperty = AvaloniaProperty.Register<DtoEditorHost, int>(nameof(Depth));
    public int Depth { get => GetValue(DepthProperty); set => SetValue(DepthProperty, value); }
    public static readonly StyledProperty<DtoEditorItem?> ItemProperty = AvaloniaProperty.Register<DtoEditorHost, DtoEditorItem?>(nameof(Item));
    public static readonly StyledProperty<EditorRegistry?> RegistryProperty = AvaloniaProperty.Register<DtoEditorHost, EditorRegistry?>(nameof(Registry));
    public static readonly StyledProperty<ControlTheme?> FormThemeProperty = AvaloniaProperty.Register<DtoEditorHost, ControlTheme?>(nameof(FormTheme));
    public static readonly DirectProperty<DtoEditorHost, IReadOnlyList<ValueIssue>> DiagnosticsProperty = AvaloniaProperty.RegisterDirect<DtoEditorHost, IReadOnlyList<ValueIssue>>(nameof(Diagnostics), host => host.Diagnostics);
    private IReadOnlyList<ValueIssue> _diagnostics = [];
    private DtoEditorContext? _context;
    private FieldEditorLease? _lease;
    private bool _mounted;
    public DtoEditorItem? Item { get => GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public EditorRegistry? Registry { get => GetValue(RegistryProperty); set => SetValue(RegistryProperty, value); }
    public ControlTheme? FormTheme { get => GetValue(FormThemeProperty); set => SetValue(FormThemeProperty, value); }
    public IReadOnlyList<ValueIssue> Diagnostics => _diagnostics;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _mounted = true; RefreshEditor(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _mounted = false; SetAndRaise(DiagnosticsProperty, ref _diagnostics, ReleaseEditor()); base.OnDetachedFromVisualTree(e); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemProperty || change.Property == RegistryProperty || change.Property == FormThemeProperty || change.Property == DisplayModeProperty) RefreshEditor();
    }
    public void RefreshEditor()
    {
        if (!_mounted) return;
        var errors = ReleaseEditor().ToList();
        try
        {
            if (Item is { } item)
            {
                var registry = Registry ?? throw new InvalidOperationException("DTO editor requires a registry.");
                _context = new(item, registry, FormTheme, Depth, DisplayMode);
                _lease = registry.CreateDto(_context) ?? throw new InvalidOperationException("DTO factory returned no lease.");
                if (_lease.Control.Parent is not null || _lease.Control.GetVisualParent() is not null) throw new InvalidOperationException("DTO factory must return a fresh unparented control.");
                Content = _lease.Control;
            }
        }
        catch (Exception exception)
        {
            errors.Add(new("dto.create", exception.Message, Item?.Id)); errors.AddRange(ReleaseEditor());
            Content = new TextBlock { Text = exception.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        }
        SetAndRaise(DiagnosticsProperty, ref _diagnostics, errors.AsReadOnly());
    }
    private IReadOnlyList<ValueIssue> ReleaseEditor()
    {
        var context = _context; var lease = _lease; _context = null; _lease = null;
        var errors = new List<ValueIssue>();
        try { context?.Dispose(); } catch (Exception exception) { errors.Add(new("dto.release", exception.Message, Item?.Id)); }
        Content = null;
        try { lease?.Dispose(); } catch (Exception exception) { errors.Add(new("dto.release", exception.Message, Item?.Id)); }
        return errors.AsReadOnly();
    }
}
