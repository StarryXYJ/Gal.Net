using System.Text.Json;
using GalNet.Core.Parameters;

namespace GalNet.Game.Controls.Forms;

public delegate FieldEditorLease FieldEditorFactory(FieldEditorContext context);

/// <summary>Host-scoped, explicit registration. Mutations are reflected on the next form rebuild.</summary>
public sealed class EditorRegistry
{
    private readonly Dictionary<string, FieldEditorFactory> _factories = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> _defaults = [];

    public void Register(string id, FieldEditorFactory factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        if (!_factories.TryAdd(id, factory)) throw new ArgumentException($"Editor '{id}' is already registered.", nameof(id));
    }

    public void Replace(string id, FieldEditorFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!_factories.ContainsKey(id)) throw new KeyNotFoundException($"Editor '{id}' is not registered.");
        _factories[id] = factory;
    }

    public void RegisterDefault(Type type, string editorId)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!_factories.ContainsKey(editorId)) throw new KeyNotFoundException($"Editor '{editorId}' is not registered.");
        if (!_defaults.TryAdd(type, editorId)) throw new ArgumentException($"A default editor for '{type.Name}' is already registered.");
    }

    public FieldEditorFactory Resolve(FieldDescriptor field)
    {
        var type = Nullable.GetUnderlyingType(field.Value.ValueType) ?? field.Value.ValueType;
        var id = field.Editor?.EditorId;
        if (id is null)
            id = field.Value.TryGetConstraint("enum", out _) || field.Value.TryGetConstraint("options", out _)
                ? ValueEditorIds.Choice : _defaults.TryGetValue(type, out var mapped) ? mapped
                    : type.IsEnum ? ValueEditorIds.Choice : ValueEditorIds.Json;
        return _factories.TryGetValue(id, out var factory) ? factory
            : throw new NotSupportedException($"Editor '{id}' for field '{field.Id}' is not registered.");
    }

    public static EditorRegistry CreateDefault()
    {
        var registry = new EditorRegistry();
        registry.Register(ValueEditorIds.Text, DefaultFieldEditors.CreateText);
        registry.Register(ValueEditorIds.Numeric, DefaultFieldEditors.CreateNumeric);
        registry.Register(ValueEditorIds.Slider, DefaultFieldEditors.CreateSlider);
        registry.Register(ValueEditorIds.Toggle, DefaultFieldEditors.CreateToggle);
        registry.Register(ValueEditorIds.Choice, DefaultFieldEditors.CreateChoice);
        registry.Register(ValueEditorIds.Json, DefaultFieldEditors.CreateJson);
        registry.RegisterDefault(typeof(string), ValueEditorIds.Text);
        registry.RegisterDefault(typeof(bool), ValueEditorIds.Toggle);
        foreach (var type in DefaultFieldEditors.NumericTypes) registry.RegisterDefault(type, ValueEditorIds.Numeric);
        registry.RegisterDefault(typeof(JsonElement), ValueEditorIds.Json);
        return registry;
    }
}
