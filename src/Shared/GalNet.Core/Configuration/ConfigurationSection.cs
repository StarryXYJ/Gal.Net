using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Parameters;

namespace GalNet.Core.Configuration;

public abstract class ConfigurationSection
{
    protected ConfigurationSection(string id, IReadOnlyList<FieldDescriptor> fields, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        if (fields.Select(field => field.Id).Distinct(StringComparer.Ordinal).Count() != fields.Count)
            throw new ArgumentException("Configuration field IDs must be unique.", nameof(fields));
        Id = id;
        Version = version;
        Fields = Array.AsReadOnly(fields.ToArray());
    }

    public string Id { get; }
    public int Version { get; }
    public IReadOnlyList<FieldDescriptor> Fields { get; }
    public abstract Type DataType { get; }
    public abstract JsonElement DefaultData { get; }
    public abstract void Validate(ProjectSectionData value);
    protected void ValidateFields(ProjectSectionData value)
    {
        if (value.Version != Version) throw new InvalidDataException($"Unsupported configuration version {value.Version} for '{Id}'; expected {Version}.");
        if (value.Data.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"Configuration section '{Id}' must contain an object.");
        foreach (var field in Fields)
        {
            if (value.Data.TryGetProperty(field.Id, out var json))
            {
                if (ValueValidation.Validate(field.Value, json) is { } issue)
                    throw new InvalidDataException($"Configuration '{Id}/{field.Id}': {issue.Message}");
            }
            else if (field.IsRequired && field.DefaultValue is null)
                throw new InvalidDataException($"Configuration '{Id}' requires '{field.Id}'.");
        }
    }

    public static ConfigurationSection<T> ForDto<T>(string id, Action<T>? validate = null, int version = 1) where T : IFieldDto, IFieldSchema<T>, new() =>
        new(id, T.Schema, validate, version);
}

public sealed class ConfigurationSection<T>(string id, IReadOnlyList<FieldDescriptor> fields, Action<T>? validate = null, int version = 1)
    : ConfigurationSection(id, fields, version) where T : new()
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, Converters = { new JsonStringEnumConverter() } };
    public override Type DataType => typeof(T);
    public override JsonElement DefaultData => Encode(CreateDefault());
    public T CreateDefault()
    {
        var value = new T();
        if (value is IFieldDto dto)
        {
            foreach (var field in Fields)
                if (field.DefaultValue is { } fallback) dto.WriteField(field.Id, ValueCodec.Deserialize(field.Value, fallback));
        }
        else if (Fields.Any(field => field.DefaultValue is not null))
        {
            var data = JsonSerializer.SerializeToElement(value, JsonOptions).EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
            foreach (var field in Fields)
                if (field.DefaultValue is { } fallback) data[field.Id] = fallback;
            value = JsonSerializer.SerializeToElement(data).Deserialize<T>(JsonOptions)!;
        }
        _ = Encode(value);
        return value;
    }

    public JsonElement Encode(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var data = value is IFieldDto dto ? ValueCodec.SerializeFields(dto) : JsonSerializer.SerializeToElement(value, JsonOptions);
        ValidateFields(new(Version, data));
        validate?.Invoke(value);
        return data;
    }

    public T Read(ProjectSectionData value)
    {
        ValidateFields(value);
        var normalized = value.Data.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        foreach (var field in Fields)
            if (!normalized.ContainsKey(field.Id) && field.DefaultValue is { } fallback) normalized.Add(field.Id, fallback);
        var data = JsonSerializer.SerializeToElement(normalized);
        var result = new T();
        if (result is IFieldDto dto)
        {
            foreach (var field in Fields)
                if (data.TryGetProperty(field.Id, out var json)) dto.WriteField(field.Id, ValueCodec.Deserialize(field.Value, json));
        }
        else result = data.Deserialize<T>(JsonOptions) ?? throw new InvalidDataException($"Configuration '{Id}' cannot be null.");
        validate?.Invoke(result);
        return result;
    }

    public override void Validate(ProjectSectionData value) => _ = Read(value);
}

/// <summary>Explicit project contract declarations; unknown sections remain opaque author data.</summary>
public sealed class ProjectConfigurationSchema
{
    public static ProjectConfigurationSchema Empty { get; } = new([]);
    public IReadOnlyList<ConfigurationSection> Sections { get; }
    public ProjectConfigurationSchema(IEnumerable<ConfigurationSection> sections)
    {
        var values = sections.ToArray();
        if (values.Any(section => section is null) || values.Select(section => section.Id).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("Configuration section IDs must be nonempty and unique.", nameof(sections));
        Sections = Array.AsReadOnly(values);
    }

    public void Validate(ProjectConfiguration configuration)
    {
        var stored = configuration.Sections;
        foreach (var section in Sections)
            if (stored.TryGetValue(section.Id, out var value)) section.Validate(value);
    }
}
