using GalNet.Core.Serialization;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalNet.Core.Configuration;

public sealed record ProjectSectionData(int Version, JsonElement Data);

/// <summary>Owned author/content data. Defaults are read without mutation; runtime modules receive projected options.</summary>
[JsonConverter(typeof(ProjectConfigurationJsonConverter))]
public sealed class ProjectConfiguration
{
    public const int CurrentVersion = 1;
    private Dictionary<string, ProjectSectionData> _sections = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, ProjectSectionData> Sections => new ReadOnlyDictionary<string, ProjectSectionData>(_sections);

    public T Read<T>(ConfigurationSection<T> section) where T : new() =>
        section.Read(_sections.GetValueOrDefault(section.Id));

    public void Set<T>(ConfigurationSection<T> section, T value) where T : new()
    {
        ArgumentNullException.ThrowIfNull(value);
        var data = section.Encode(value);
        if (_sections.TryGetValue(section.Id, out var previous))
        {
            if (previous.Version != section.Version)
                throw new InvalidDataException($"Unsupported configuration version {previous.Version} for '{section.Id}'; expected {section.Version}.");
            // A typed editor owns declared members; retain fields it doesn't understand.
            var merged = previous.Data.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
            foreach (var property in data.EnumerateObject()) merged[property.Name] = property.Value;
            data = JsonCodec.SerializeToElement(merged);
        }
        section.Validate(new(section.Version, data));
        SetRaw(section.Id, new(section.Version, data));
    }

    public void SetRaw(string id, ProjectSectionData value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Version < 1 || value.Data.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Configuration section '{id}' requires a positive version and object data.");
        using var document = JsonDocument.Parse(value.Data.GetRawText(), new JsonDocumentOptions { AllowDuplicateProperties = false });
        _sections[id] = value with { Data = document.RootElement.Clone() };
    }

    public bool Remove(string id) => _sections.Remove(id);
    public void Patch(ConfigurationSection section, IReadOnlyDictionary<string, JsonElement> values, IReadOnlyList<string>? remove = null)
    {
        var previous = _sections.GetValueOrDefault(section.Id);
        if (previous is not null && previous.Version != section.Version)
            throw new InvalidDataException($"Unsupported configuration version {previous.Version} for '{section.Id}'.");
        var fields = section.Fields.Select(field => field.Id).ToHashSet(StringComparer.Ordinal);
        if (values.Keys.Concat(remove ?? []).Any(id => !fields.Contains(id)))
            throw new ArgumentException($"The patch contains undeclared fields in '{section.Id}'.");
        var candidate = previous?.Data.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal) ?? new();
        foreach (var id in remove ?? []) candidate.Remove(id);
        foreach (var (id, value) in values) candidate[id] = value;
        var data = new ProjectSectionData(section.Version, JsonCodec.SerializeToElement(candidate));
        section.Validate(data);
        SetRaw(section.Id, data);
    }

    public ProjectConfiguration Clone()
    {
        var clone = new ProjectConfiguration();
        clone.ReplaceFrom(this);
        return clone;
    }

    public void ReplaceFrom(ProjectConfiguration source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _sections = source._sections.ToDictionary(pair => pair.Key, pair => pair.Value with { Data = pair.Value.Data.Clone() }, StringComparer.Ordinal);
    }
}

public sealed class ProjectConfigurationJsonConverter : JsonConverter<ProjectConfiguration>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        AllowDuplicateProperties = false,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    internal sealed record Envelope(int Version, IReadOnlyDictionary<string, ProjectSectionData> Sections);

    public override ProjectConfiguration Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var envelope = JsonCodec.Deserialize<Envelope>(ref reader, JsonOptions);
        if (envelope is not { Version: ProjectConfiguration.CurrentVersion, Sections: not null })
            throw new JsonException("Project configuration requires version 1 and an object of sections.");
        var result = new ProjectConfiguration();
        foreach (var (id, section) in envelope.Sections)
            result.SetRaw(id, section ?? throw new JsonException($"Invalid configuration section '{id}'."));
        return result;
    }

    public override void Write(Utf8JsonWriter writer, ProjectConfiguration value, JsonSerializerOptions options) =>
        JsonCodec.Serialize(writer, new Envelope(ProjectConfiguration.CurrentVersion,
            value.Sections.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary()), JsonOptions);
}
