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
        var definitions = shader.Parameters.Select(parameter => (Parameter: parameter, Field: ShaderEffectFields.Create(parameter))).ToArray();
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
                return new("shader.parameters", "Shader parameters must be an object.");
            var values = source.ValueKind == JsonValueKind.Object
                ? source.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal)
                : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (value.Json is { } json) values[descriptor.Id] = json.Clone(); else values.Remove(descriptor.Id);
            return await form._owner.CommitParameterAsync(form._root, new(JsonSerializer.SerializeToElement(values)), cancellationToken);
        }
    }
}
