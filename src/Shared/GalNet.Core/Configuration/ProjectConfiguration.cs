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
        _sections.TryGetValue(section.Id, out var stored) ? section.Read(stored) : section.CreateDefault();

    public void Set<T>(ConfigurationSection<T> section, T value) where T : new()
    {
        var data = section.Encode(value);
        if (_sections.TryGetValue(section.Id, out var previous))
        {
            if (previous.Version != section.Version)
                throw new InvalidDataException($"Unsupported configuration version {previous.Version} for '{section.Id}'; expected {section.Version}.");
            // A typed editor owns declared members; retain fields it doesn't understand.
            var merged = previous.Data.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
            foreach (var property in data.EnumerateObject()) merged[property.Name] = property.Value;
            data = JsonSerializer.SerializeToElement(merged);
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
        ProjectConfigurationJsonConverter.RequireUniqueProperties(value.Data);
        _sections[id] = value with { Data = value.Data.Clone() };
    }

    public bool Remove(string id) => _sections.Remove(id);
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
    public override ProjectConfiguration Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        RequireUniqueProperties(root);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != ProjectConfiguration.CurrentVersion ||
            !root.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Object ||
            root.EnumerateObject().Any(property => property.Name is not ("version" or "sections")))
            throw new JsonException("Project configuration requires version 1 and an object of sections.");
        var result = new ProjectConfiguration();
        foreach (var property in sections.EnumerateObject())
        {
            var envelope = property.Value;
            if (envelope.ValueKind != JsonValueKind.Object || !envelope.TryGetProperty("version", out var sectionVersion) ||
                sectionVersion.ValueKind != JsonValueKind.Number || !sectionVersion.TryGetInt32(out var sectionNumber) || !envelope.TryGetProperty("data", out var data) ||
                envelope.EnumerateObject().Any(member => member.Name is not ("version" or "data")))
                throw new JsonException($"Invalid configuration section '{property.Name}'.");
            result.SetRaw(property.Name, new(sectionNumber, data));
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, ProjectConfiguration value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("version", ProjectConfiguration.CurrentVersion);
        writer.WriteStartObject("sections");
        foreach (var (id, section) in value.Sections.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WriteStartObject(id);
            writer.WriteNumber("version", section.Version);
            writer.WritePropertyName("data");
            section.Data.WriteTo(writer);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    internal static void RequireUniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException($"Duplicate configuration property '{property.Name}'.");
                RequireUniqueProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RequireUniqueProperties(item);
    }
}
