using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Parameters;

namespace GalNet.Game.Controls.Forms;

public delegate FieldEditorLease FieldEditorFactory(FieldEditorContext context);

/// <summary>Host-scoped, explicit registration. Mutations are reflected on the next form rebuild.</summary>
public sealed class EditorRegistry
{
    private readonly Dictionary<string, FieldEditorFactory> _factories = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> _defaults = [];
    private readonly Dictionary<Type, FieldEditorFactory> _typeFactories = [];
    private readonly Dictionary<(Type Type, string Field), FieldEditorFactory> _members = [];
    private readonly Dictionary<Type, Func<DtoEditorContext, FieldEditorLease>> _dtos = [];
    public bool IsFrozen { get; private set; }
    public EditorRegistry Freeze() { IsFrozen = true; return this; }
    private void CheckMutable() { if (IsFrozen) throw new InvalidOperationException("The editor registry is frozen."); }

    public void RegisterDefault<T>(FieldEditorFactory factory)
    {
        CheckMutable(); ArgumentNullException.ThrowIfNull(factory);
        var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (_defaults.ContainsKey(type) || !_typeFactories.TryAdd(type, factory)) throw new ArgumentException($"A default editor for '{type.Name}' is already registered.");
    }
    public void ReplaceDefault<T>(FieldEditorFactory factory)
    {
        CheckMutable(); ArgumentNullException.ThrowIfNull(factory);
        var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (!_defaults.ContainsKey(type) && !_typeFactories.ContainsKey(type)) throw new KeyNotFoundException($"No default editor for '{type.Name}'.");
        _typeFactories[type] = factory;
    }
    public void RegisterMember<T>(Expression<Func<T, object?>> member, FieldEditorFactory factory) => AddMember(member, factory, false);
    public void ReplaceMember<T>(Expression<Func<T, object?>> member, FieldEditorFactory factory) => AddMember(member, factory, true);
    private void AddMember<T>(Expression<Func<T, object?>> member, FieldEditorFactory factory, bool replace)
    {
        CheckMutable(); ArgumentNullException.ThrowIfNull(member); ArgumentNullException.ThrowIfNull(factory);
        var expression = member.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : member.Body;
        if (expression is not MemberExpression { Member: PropertyInfo property } access || access.Expression != member.Parameters[0] || property.GetCustomAttribute<FieldAttribute>() is null)
            throw new ArgumentException("Select a direct generated DTO field property.", nameof(member));
        var id = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
        var key = (typeof(T), id);
        if (replace ? !_members.ContainsKey(key) : _members.ContainsKey(key)) throw new ArgumentException("Member registration does not match the requested add/replace operation.", nameof(member));
        _members[key] = factory;
    }
    public void RegisterDto<T>(Func<DtoEditorContext, FieldEditorLease> factory) => AddDto(typeof(T), factory, false);
    public void ReplaceDto<T>(Func<DtoEditorContext, FieldEditorLease> factory) => AddDto(typeof(T), factory, true);
    private void AddDto(Type type, Func<DtoEditorContext, FieldEditorLease> factory, bool replace)
    {
        CheckMutable(); ArgumentNullException.ThrowIfNull(factory);
        if (replace ? !_dtos.ContainsKey(type) : _dtos.ContainsKey(type)) throw new ArgumentException("DTO registration does not match the requested add/replace operation.", nameof(type));
        _dtos[type] = factory;
    }
    internal FieldEditorLease CreateDto(DtoEditorContext context) => context.Item.DtoType is { } type && _dtos.TryGetValue(type, out var factory)
        ? factory(context) : new(context.CreateDefaultForm());

    public void Register(string id, FieldEditorFactory factory)
    {
        CheckMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        if (!_factories.TryAdd(id, factory)) throw new ArgumentException($"Editor '{id}' is already registered.", nameof(id));
    }

    public void Replace(string id, FieldEditorFactory factory)
    {
        CheckMutable();
        ArgumentNullException.ThrowIfNull(factory);
        if (!_factories.ContainsKey(id)) throw new KeyNotFoundException($"Editor '{id}' is not registered.");
        _factories[id] = factory;
    }

    public void RegisterDefault(Type type, string editorId)
    {
        CheckMutable();
        ArgumentNullException.ThrowIfNull(type);
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (!_factories.ContainsKey(editorId)) throw new KeyNotFoundException($"Editor '{editorId}' is not registered.");
        if (_typeFactories.ContainsKey(type) || !_defaults.TryAdd(type, editorId)) throw new ArgumentException($"A default editor for '{type.Name}' is already registered.");
    }

    public FieldEditorFactory Resolve(FieldDescriptor field, Type? dtoType = null)
    {
        if (dtoType is not null && _members.TryGetValue((dtoType, field.Id), out var member)) return member;
        var type = Nullable.GetUnderlyingType(field.Value.ValueType) ?? field.Value.ValueType;
        if (_typeFactories.TryGetValue(type, out var custom)) return custom;
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
