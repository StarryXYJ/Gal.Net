using GalNet.Core.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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
    public abstract void Validate(ProjectSectionData value);
    public abstract JsonElement Resolve(ProjectSectionData? value);
}

public sealed class ConfigurationSection<T>(string id, IReadOnlyList<FieldDescriptor> fields, Action<T>? validate = null, int version = 1,
    IJsonTypeInfoResolver? jsonResolver = null)
    : ConfigurationSection(id, fields, version) where T : new()
{
    private readonly JsonSerializerOptions _jsonOptions = JsonMetadata.CreateOptions(new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false }, jsonResolver, stringEnums: true);
    internal JsonElement Encode(T value) => JsonCodec.SerializeToElement(value, _jsonOptions);

    public T Read(ProjectSectionData? value)
    {
        if (value is not null && value.Version != Version) throw new InvalidDataException($"Unsupported configuration version {value.Version} for '{Id}'; expected {Version}.");
        var data = value?.Data ?? Encode(new T());
        if (data.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"Configuration section '{Id}' must contain an object.");
        var normalized = data.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            if (field.DefaultValue is { } fallback && (value is null || !normalized.ContainsKey(field.Id))) normalized[field.Id] = fallback;
            if (!normalized.TryGetValue(field.Id, out var json))
            {
                if (field.IsRequired) throw new InvalidDataException($"Configuration '{Id}' requires '{field.Id}'.");
                else continue;
            }
            if (ValueValidation.Validate(field.Value, json) is { } issue)
                throw new InvalidDataException($"Configuration '{Id}/{field.Id}': {issue.Message}");
        }
        var result = JsonCodec.Deserialize<T>(JsonCodec.SerializeToElement(normalized), _jsonOptions)
            ?? throw new InvalidDataException($"Configuration '{Id}' cannot be null.");
        validate?.Invoke(result);
        return result;
    }

    public override void Validate(ProjectSectionData value) => _ = Read(value);
    public override JsonElement Resolve(ProjectSectionData? value) => Encode(Read(value));
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
