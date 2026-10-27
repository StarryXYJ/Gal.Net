using System.Globalization;
using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Parameters;
using GalNet.Editor.Models.Graph;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Inspector.ViewModels;

/// <summary>Projects shader metadata onto one effect.apply parameters object without owning author data.</summary>
public sealed class ShaderParameterFormBinding : IDisposable
{
    private readonly EntryEditorItemViewModel _owner;
    private readonly FieldDescriptor _root;
    private readonly string _program;
    private EventHandler? _changed;
    private bool _disposed;

    public ShaderParameterFormBinding(EntryEditorItemViewModel owner, FieldDescriptor root, ShaderEffectDescriptor shader)
    {
        _owner = owner;
        _root = root;
        _program = shader.Resource.Value;
        var definitions = shader.Parameters.Select(parameter => (Parameter: parameter, Field: CreateField(parameter))).ToArray();
        var bindings = definitions.ToDictionary(item => item.Field.Id,
            item => (IFieldBinding)new ParameterBinding(this, item.Parameter, item.Field), StringComparer.Ordinal);
        Item = new(owner.StableId + "/shader", null, definitions.Select(item => item.Field).ToArray(), field => bindings[field.Id]);
    }

    public DtoEditorItem Item { get; }
    private bool IsCurrent => !_disposed && _owner.IsCurrent && _owner.Type == BuiltinEntryIds.ApplyEffect
        && _owner.Source.Parameters.TryGetValue("program", out var program) && program.ValueKind == JsonValueKind.String
        && program.GetString() == _program;

    public void Refresh() { if (!_disposed) _changed?.Invoke(this, EventArgs.Empty); }
    public void Dispose() { _disposed = true; _changed = null; }

    private static FieldDescriptor CreateField(ShaderEffectParameterDescriptor parameter)
    {
        var type = parameter.Kind switch
        {
            ShaderEffectParameterKind.Float => typeof(float),
            ShaderEffectParameterKind.Integer => typeof(int),
            ShaderEffectParameterKind.Boolean => typeof(bool),
            ShaderEffectParameterKind.Vector2 or ShaderEffectParameterKind.Vector3 or ShaderEffectParameterKind.Vector4 => typeof(float[]),
            _ => typeof(string)
        };
        var constraints = new Dictionary<string, object>();
        if (parameter.Minimum is { } minimum && type != typeof(float[])) constraints["minimum"] = minimum;
        if (parameter.Maximum is { } maximum && type != typeof(float[])) constraints["maximum"] = maximum;
        if (parameter.Kind == ShaderEffectParameterKind.Enum && parameter.Options is { Count: > 0 } options)
            constraints["enum"] = options;
        if (parameter.Kind is ShaderEffectParameterKind.Vector2 or ShaderEffectParameterKind.Vector3 or ShaderEffectParameterKind.Vector4)
            constraints["minItems"] = constraints["maxItems"] = parameter.Kind switch
            {
                ShaderEffectParameterKind.Vector2 => 2,
                ShaderEffectParameterKind.Vector3 => 3,
                _ => 4
            };
        var descriptor = new ValueDescriptor(type, allowNull: false, constraints: JsonSerializer.SerializeToElement(constraints));
        var editor = parameter.Kind switch
        {
            ShaderEffectParameterKind.Texture => "entry.imageasset",
            ShaderEffectParameterKind.Color => "shader.color",
            ShaderEffectParameterKind.Vector2 => "shader.vector2",
            ShaderEffectParameterKind.Vector3 => "shader.vector3",
            ShaderEffectParameterKind.Vector4 => "shader.vector4",
            ShaderEffectParameterKind.Boolean => ValueEditorIds.Toggle,
            ShaderEffectParameterKind.Enum => ValueEditorIds.Choice,
            ShaderEffectParameterKind.Float or ShaderEffectParameterKind.Integer => ValueEditorIds.Numeric,
            _ => null
        };
        var fallback = Default(parameter, descriptor);
        return new(parameter.Name, descriptor, parameter.Required, fallback, new EditorHints(editor, parameter.Step));
    }

    private static JsonElement? Default(ShaderEffectParameterDescriptor parameter, ValueDescriptor descriptor)
    {
        if (parameter.DefaultValue is not { } text) return null;
        try
        {
            object value = parameter.Kind switch
            {
                ShaderEffectParameterKind.Float => float.Parse(text, CultureInfo.InvariantCulture),
                ShaderEffectParameterKind.Integer => int.Parse(text, CultureInfo.InvariantCulture),
                ShaderEffectParameterKind.Boolean => bool.Parse(text),
                ShaderEffectParameterKind.Vector2 or ShaderEffectParameterKind.Vector3 or ShaderEffectParameterKind.Vector4 => JsonSerializer.Deserialize<float[]>(text)!,
                _ => text
            };
            var json = JsonSerializer.SerializeToElement(value, descriptor.ValueType, descriptor.JsonOptions);
            return ValueValidation.Validate(descriptor, json) is null ? json : null;
        }
        catch (Exception error) when (error is JsonException or FormatException or OverflowException or ArgumentException)
        { return null; }
    }

    private sealed class ParameterBinding(ShaderParameterFormBinding form, ShaderEffectParameterDescriptor parameter, FieldDescriptor descriptor) : IFieldBinding
    {
        public FieldDraftState? Draft => form.IsCurrent ? form._owner.EditSession?.Get(form._owner, form._root).Child?.Invoke(descriptor, descriptor.Id) : null;
        public object? Label => string.IsNullOrWhiteSpace(parameter.Group) ? parameter.DisplayName ?? descriptor.Id
            : parameter.Group + " / " + (parameter.DisplayName ?? descriptor.Id);
        public bool IsEnabled => form.IsCurrent;
        public FieldValue Read()
        {
            if (form._owner.Source.Parameters.TryGetValue("parameters", out var root)
                && root.ValueKind == JsonValueKind.Object && root.TryGetProperty(descriptor.Id, out var value)) return new(value);
            return default;
        }
        public event EventHandler? Changed
        {
            add { ObjectDisposedException.ThrowIf(form._disposed, form); form._changed += value; }
            remove { form._changed -= value; }
        }
        public async Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!form.IsCurrent) return new("shader.obsolete", "The shader selection changed.", descriptor.Id);
            if (value.Json is not { } && descriptor.IsRequired && descriptor.DefaultValue is null)
                return new("shader.required", "A value is required.", descriptor.Id);
            if (value.Json is { } candidate && ValueValidation.Validate(descriptor.Value, candidate) is { } issue) return issue;
            var source = form._owner.Source.Parameters.GetValueOrDefault("parameters");
            if (source.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object))
                return new("shader.parameters", "Repair the parameters JSON object before editing its fields.");
            var values = source.ValueKind == JsonValueKind.Object
                ? source.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal)
                : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (value.Json is { } json) values[descriptor.Id] = json.Clone(); else values.Remove(descriptor.Id);
            return await form._owner.CommitParameterAsync(form._root, new(JsonSerializer.SerializeToElement(values)), cancellationToken);
        }
    }
}
