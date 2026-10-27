using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Configuration;
using GalNet.Core.I18n;
using GalNet.Core.Parameters;

namespace GalNet.Contracts.Builtins;

/// <summary>Optional default host authoring model, selected by templates and sample composition.</summary>
public static class DefaultProjectConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, Converters = { new JsonStringEnumConverter() } };
    public static ConfigurationSection<DefaultProjectOptions> Section { get; } = new("galnet.project",
    [
        Field("targetLocale", typeof(I18nLocale)), Field("availableLocales", typeof(List<I18nLocale>)),
        Field("defaultWidth", typeof(int), minimum: 1), Field("defaultHeight", typeof(int), minimum: 1),
        Field("saveSlotCount", typeof(int), minimum: 0),
        Field("audioBuses", typeof(List<AudioBusDefinition>)), Field("audioTracks", typeof(List<AudioTrackDefinition>)),
        Field("defaultVisualPrograms", typeof(Dictionary<string, string>))
    ], value => _ = new AudioConfiguration(value.AudioTracks, value.AudioBuses), version: 2);
    public static ProjectConfigurationSchema Schema { get; } = new([Section]);

    public static DefaultProjectOptions ReadDefaultOptions(this ProjectConfiguration configuration) => configuration.Read(Section);
    public static ProjectConfiguration ToConfiguration(this DefaultProjectOptions value)
    {
        var configuration = new ProjectConfiguration();
        configuration.Set(Section, value);
        return configuration;
    }

    private static FieldDescriptor Field(string id, Type type, int? minimum = null) => new(id,
        new ValueDescriptor(type, allowNull: false,
            constraints: minimum is { } bound ? JsonSerializer.SerializeToElement(new { minimum = bound }) : null,
            jsonOptions: JsonOptions), editor: new EditorHints(EditorId: type == typeof(int) ? ValueEditorIds.Numeric : ValueEditorIds.Json));
}
