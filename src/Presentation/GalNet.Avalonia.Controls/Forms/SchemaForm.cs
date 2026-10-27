using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using GalNet.Core.Parameters;

namespace GalNet.Game.Controls.Forms;

public sealed record FieldEditorItem(FieldEditorContext Context, Control Control)
{
    public bool IsJsonFallback => Equals(Control.Tag, "schema.json");
}

/// <summary>Owns editors while mounted. Schema, registry, binding and visual theme are supplied by the host.</summary>
public sealed class SchemaForm : TemplatedControl
{
    public static readonly StyledProperty<bool> ShowLabelsProperty = AvaloniaProperty.Register<SchemaForm, bool>(nameof(ShowLabels), true);
    public bool ShowLabels { get => GetValue(ShowLabelsProperty); set => SetValue(ShowLabelsProperty, value); }
    public static readonly StyledProperty<int> DepthProperty = AvaloniaProperty.Register<SchemaForm, int>(nameof(Depth));
    public int Depth { get => GetValue(DepthProperty); set => SetValue(DepthProperty, value); }
    public static readonly StyledProperty<Type?> DtoTypeProperty = AvaloniaProperty.Register<SchemaForm, Type?>(nameof(DtoType));
    public Type? DtoType { get => GetValue(DtoTypeProperty); set => SetValue(DtoTypeProperty, value); }
    public static readonly StyledProperty<IReadOnlyList<FieldDescriptor>?> SchemaProperty =
        AvaloniaProperty.Register<SchemaForm, IReadOnlyList<FieldDescriptor>?>(nameof(Schema));
    public static readonly StyledProperty<EditorRegistry?> RegistryProperty =
        AvaloniaProperty.Register<SchemaForm, EditorRegistry?>(nameof(Registry));
    public static readonly StyledProperty<Func<FieldDescriptor, IFieldBinding>?> BindingProperty =
        AvaloniaProperty.Register<SchemaForm, Func<FieldDescriptor, IFieldBinding>?>(nameof(Binding));
    public static readonly DirectProperty<SchemaForm, IReadOnlyList<FieldEditorItem>> EditorsProperty =
        AvaloniaProperty.RegisterDirect<SchemaForm, IReadOnlyList<FieldEditorItem>>(nameof(Editors), form => form.Editors);
    public static readonly DirectProperty<SchemaForm, IReadOnlyList<ValueIssue>> DiagnosticsProperty =
        AvaloniaProperty.RegisterDirect<SchemaForm, IReadOnlyList<ValueIssue>>(nameof(Diagnostics), form => form.Diagnostics);

    private IReadOnlyList<FieldEditorItem> _editors = [];
    private IReadOnlyList<ValueIssue> _diagnostics = [];
    private List<(FieldEditorContext Context, FieldEditorLease? Lease)> _owned = [];
    private bool _mounted;
    protected override Type StyleKeyOverride => typeof(SchemaForm);
    public IReadOnlyList<FieldDescriptor>? Schema { get => GetValue(SchemaProperty); set => SetValue(SchemaProperty, value); }
    public EditorRegistry? Registry { get => GetValue(RegistryProperty); set => SetValue(RegistryProperty, value); }
    public Func<FieldDescriptor, IFieldBinding>? Binding { get => GetValue(BindingProperty); set => SetValue(BindingProperty, value); }
    public IReadOnlyList<FieldEditorItem> Editors => _editors;
    public IReadOnlyList<ValueIssue> Diagnostics => _diagnostics;
    public IReadOnlyList<FieldEditorContext> Contexts => Array.AsReadOnly(_owned.Select(editor => editor.Context).ToArray());

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _mounted = true;
        RefreshEditors();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _mounted = false;
        SetAndRaise(DiagnosticsProperty, ref _diagnostics, ReleaseEditors());
        base.OnDetachedFromVisualTree(e);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SchemaProperty || change.Property == RegistryProperty || change.Property == BindingProperty || change.Property == DtoTypeProperty)
            RefreshEditors();
    }

    public void RefreshEditors()
    {
        if (!_mounted) return;
        var errors = ReleaseEditors().ToList();
        string? current = null;
        try
        {
            if (Schema is { } schema)
            {
                var fields = schema.ToArray();
                if (fields.Select(field => field.Id).Distinct(StringComparer.Ordinal).Count() != fields.Length)
                    throw new ArgumentException("Schema field IDs must be unique.");
                if (fields.Length != 0 && (Registry is null || Binding is null))
                    throw new InvalidOperationException("SchemaForm requires an explicit registry and binding.");
                var controls = new HashSet<Control>(ReferenceEqualityComparer.Instance);
                foreach (var field in fields)
                {
                    current = field.Id;
                    var context = new FieldEditorContext(field, Binding!(field), Registry, Depth);
                    _owned.Add((context, null));
                    FieldEditorLease lease;
                    try { lease = Registry!.Resolve(field, DtoType)(context) ?? throw new InvalidOperationException("Editor factory returned no lease."); }
                    catch (NotSupportedException error)
                    {
                        errors.Add(new("editor.fallback", error.Message, field.Id));
                        lease = DefaultFieldEditors.CreateJson(context);
                    }
                    _owned[^1] = (context, lease);
                    if (lease.Diagnostic is { } diagnostic) errors.Add(diagnostic with { Path = field.Id, Message = Registry!.FormatIssue?.Invoke(field, diagnostic) ?? diagnostic.Message });
                    if (!controls.Add(lease.Control) || lease.Control.Parent is not null || lease.Control.GetVisualParent() is not null)
                        throw new InvalidOperationException("Editor factories must return fresh, unparented controls.");
                }
                SetAndRaise(EditorsProperty, ref _editors, Array.AsReadOnly(_owned.Select(editor => new FieldEditorItem(editor.Context, editor.Lease!.Control)).ToArray()));
            }
        }
        catch (Exception exception)
        {
            errors.Add(new("editor.create", exception.Message, current));
            errors.AddRange(ReleaseEditors());
        }
        SetAndRaise(DiagnosticsProperty, ref _diagnostics, Array.AsReadOnly(errors.ToArray()));
    }

    private ReadOnlyCollection<ValueIssue> ReleaseEditors()
    {
        var owned = _owned;
        _owned = [];
        var errors = new List<ValueIssue>();
        foreach (var (context, _) in owned)
        {
            try { context.Dispose(); }
            catch (Exception exception) { errors.Add(new("editor.release", exception.Message, context.Field.Id)); }
        }
        foreach (var (context, lease) in owned)
        {
            try { lease?.Dispose(); }
            catch (Exception exception) { errors.Add(new("editor.release", exception.Message, context.Field.Id)); }
        }
        SetAndRaise(EditorsProperty, ref _editors, Array.Empty<FieldEditorItem>());
        return Array.AsReadOnly(errors.ToArray());
    }
}
