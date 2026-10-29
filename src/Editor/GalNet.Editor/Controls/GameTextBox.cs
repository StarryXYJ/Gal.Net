using Avalonia;
using Avalonia.Controls;
using GalNet.Core.Parameters;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>Scalar semantic text host for Graph options; uses the same editor as Group DTO fields.</summary>
public sealed class GameTextBox : ContentControl
{
    protected override Type StyleKeyOverride => typeof(ContentControl);
    public static readonly StyledProperty<IFieldBinding?> BindingProperty = AvaloniaProperty.Register<GameTextBox, IFieldBinding?>(nameof(Binding));
    public static readonly StyledProperty<GameTextAuthoringService?> AuthoringProperty = AvaloniaProperty.Register<GameTextBox, GameTextAuthoringService?>(nameof(Authoring));
    public IFieldBinding? Binding { get => GetValue(BindingProperty); set => SetValue(BindingProperty, value); }
    public GameTextAuthoringService? Authoring { get => GetValue(AuthoringProperty); set => SetValue(AuthoringProperty, value); }
    private FieldEditorContext? _context;
    private FieldEditorLease? _lease;
    private bool _attached;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _attached = true; Rebuild(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _attached = false; Release(); base.OnDetachedFromVisualTree(e); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (_attached && (change.Property == BindingProperty || change.Property == AuthoringProperty)) Rebuild(); }
    private void Rebuild()
    {
        Release();
        if (Binding is null || Authoring is null) return;
        _context = new(new("text", new(typeof(string), allowNull: false), isRequired: true, semantic: FieldValueSemantic.LocalizedText), Binding);
        _lease = GameTextFieldEditor.Create(_context, Authoring);
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        Content = new FieldEditorRow { Context = _context, Editor = _lease.Control, ShowLabel = false, RowSpacing = 0 };
    }
    private void Release() { Content = null; _lease?.Dispose(); _context?.Dispose(); _lease = null; _context = null; }
}
