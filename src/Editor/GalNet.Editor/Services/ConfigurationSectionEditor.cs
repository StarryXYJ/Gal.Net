using System.Globalization;
using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Core.Configuration;
using GalNet.Core.Parameters;
using GalNet.Game.Controls.Forms;
using GalNet.Contracts.Builtins.Audio;

namespace GalNet.Editor.Services;

/// <summary>Schema fields stage JSON in a dialog draft; publication and history belong to the project.</summary>
public sealed class ConfigurationSectionEditor : IDisposable
{
    private readonly ConfigurationSection _section;
    private readonly ProjectConfiguration _configuration;
    private readonly ProjectSectionData? _baseline;
    private readonly JsonElement _resolved;
    private readonly Func<bool> _isCurrent;
    private readonly Action _changed;
    private readonly IEditorLocalizationService _localization;
    private readonly Dictionary<string, Field> _fields;
    private bool _disposed;
    public string Id => _section.Id;
    public DtoEditorItem Item { get; }
    public EditorRegistry Registry { get; }
    public event Action? AudioBusesChanged;
    public bool HasChanges => _fields.Values.Any(item => item.Draft.IsDirty || item.Pending is { } value && !Equal(item.Source(_baseline), value));

    public ConfigurationSectionEditor(ConfigurationSection section, ProjectConfiguration configuration, EditorRegistry registry,
        IEditorLocalizationService localization, Func<bool> isCurrent, Action changed, Func<FieldDescriptor, bool> canEdit)
    {
        _section = section; _configuration = configuration; _baseline = configuration.Sections.GetValueOrDefault(section.Id);
        try { _resolved = section.Resolve(_baseline); }
        catch (Exception error) when (error is System.IO.InvalidDataException or JsonException or ArgumentException)
        { _resolved = JsonSerializer.SerializeToElement(new { }); }
        _isCurrent = isCurrent; _changed = changed; _localization = localization; Registry = registry;
        var fields = section.Fields.Where(field => section.Id != "galnet.project" || field.Id is not ("targetLocale" or "availableLocales"))
            .Select(Decorate).ToArray();
        _fields = fields.ToDictionary(field => field.Id, field => new Field(this, field, canEdit(field)), StringComparer.Ordinal);
        Item = new(section.Id, null, fields, Binding, async (patch, token) =>
        {
            foreach (var (id, value) in patch)
                if (await Binding(fields.Single(field => field.Id == id)).CommitAsync(value, token) is { } issue) return issue;
            return null;
        });
    }
    private FieldDescriptor Decorate(FieldDescriptor field)
    {
        if (_section.Id != "galnet.project") return field;
        var label = field.Id switch
        {
            "defaultWidth" => "Settings.Project.DefaultWidth",
            "defaultHeight" => "Settings.Project.DefaultHeight",
            "orientationPolicy" => "Settings.Project.OrientationPolicy",
            "defaultOrientation" => "Settings.Project.DefaultOrientation",
            "defaultVisualPrograms" => "Settings.Project.DefaultVisualPrograms",
            "saveSlotCount" => "Settings.Project.SaveSlotCount",
            "audioBuses" => "Settings.Project.AudioBuses",
            "audioTracks" => "Settings.Project.AudioTracks",
            _ => field.LabelKey
        };
        if (field.Id == "defaultVisualPrograms")
        {
            var map = field.Value.WithStructure(ValueStructure.Map(new("value", new ValueDescriptor(typeof(string)))));
            return new(field.Id, map, field.IsRequired, field.DefaultValue, new("structure"), label, field.DescriptionKey, field.Semantic, field.ResourceTypeId);
        }
        if (field.Id is not ("audioBuses" or "audioTracks"))
            return new(field.Id, field.Value, field.IsRequired, field.DefaultValue, field.Editor, label, field.DescriptionKey, field.Semantic, field.ResourceTypeId);
        var type = field.Id == "audioBuses" ? typeof(AudioBusDefinition) : typeof(AudioTrackDefinition);
        var options = field.Value.JsonOptions;
        var members = type.GetProperties().Select(property =>
        {
            var id = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            var key = id switch
            {
                "id" => type == typeof(AudioBusDefinition) ? "Settings.Project.GroupId" : "Settings.Project.TrackId", "name" => "Settings.Project.TrackName",
                "busId" => "Settings.Project.BusId", "defaultGain" => "Settings.Project.Gain",
                "maxVoices" => "Settings.Project.Voices", "maxQueuedItems" => "Settings.Project.Queue",
                "preserveDuringCatchUp" => "Settings.Project.PreserveAudio",
                "playbackOrder" => "Settings.Project.PlaybackOrder", "repeat" => "Settings.Project.Repeat",
                "overflowPolicy" => "Settings.Project.OverflowPolicy", "queueOverflowPolicy" => "Settings.Project.QueueOverflowPolicy", _ => null
            };
            return new FieldDescriptor(id, new ValueDescriptor(property.PropertyType, jsonOptions: options),
                editor: type == typeof(AudioTrackDefinition) && id == "busId" ? new EditorHints("project.audioBus") : null, labelKey: key);
        }).ToArray();
        var element = new FieldDescriptor("element", new ValueDescriptor(type, jsonOptions: options).WithStructure(ValueStructure.Object(members)));
        var value = field.Value.WithStructure(ValueStructure.Sequence(element));
        return new(field.Id, value, field.IsRequired, field.DefaultValue, new("structure"), label, field.DescriptionKey, field.Semantic, field.ResourceTypeId);
    }
    public IReadOnlyList<(string Id, string Name)> AudioBuses()
    {
        if (!_fields.TryGetValue("audioBuses", out var field) || field.Read().Json is not { ValueKind: JsonValueKind.Array } values) return [];
        return values.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            .Select(item => (item.GetProperty("id").GetString() ?? "", item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? "" : ""))
            .Where(item => item.Item1.Length > 0).DistinctBy(item => item.Item1, StringComparer.Ordinal).ToArray();
    }
    public IFieldBinding Binding(FieldDescriptor field) => _fields[field.Id];
    public void RefreshLocalization()
    {
        foreach (var field in _fields.Values) field.RefreshLocalization();
    }
    private bool ShowDefaultOrientation => !_fields.TryGetValue("orientationPolicy", out var orientationField)
        || orientationField.Read().Json is not { ValueKind: JsonValueKind.String } value
        || value.GetString() == nameof(ProjectOrientationPolicy.Unforced);
    private void RefreshDefaultOrientationVisibility()
    {
        if (_fields.TryGetValue("defaultOrientation", out var field)) field.NotifyChanged();
    }
    private IReadOnlyList<FieldEditorOption>? Options(FieldDescriptor descriptor)
    {
        if (_section.Id != DefaultProjectConfiguration.Section.Id) return null;
        return descriptor.Id switch
        {
            "orientationPolicy" => Choices(
                (nameof(ProjectOrientationPolicy.ForceLandscape), "Settings.Project.Orientation.ForceLandscape"),
                (nameof(ProjectOrientationPolicy.ForcePortrait), "Settings.Project.Orientation.ForcePortrait"),
                (nameof(ProjectOrientationPolicy.Unforced), "Settings.Project.Orientation.Unforced")),
            "defaultOrientation" => Choices(
                (nameof(ProjectDefaultOrientation.Landscape), "Settings.Project.Orientation.Landscape"),
                (nameof(ProjectDefaultOrientation.Portrait), "Settings.Project.Orientation.Portrait")),
            _ => null
        };
    }
    private IReadOnlyList<FieldEditorOption> Choices(params (string Value, string Label)[] values) =>
        Array.AsReadOnly(values.Select((choice, index) => new FieldEditorOption(index.ToString(CultureInfo.InvariantCulture), _localization[choice.Label],
            JsonSerializer.SerializeToElement(choice.Value))).ToArray());
    public async Task FlushAsync()
    {
        foreach (var field in _fields.Values.Where(field => field.Draft.IsDirty))
        {
            using var context = new FieldEditorContext(field.Descriptor, field, Registry);
            if (!await context.CommitTextAsync()) throw new EditorOperationException("settings.invalid", context.Issue?.Message ?? "Finish or cancel the field input before saving.");
        }
    }
    public void Apply(ProjectConfiguration candidate)
    {
        var changes = _fields.Values.Where(field => field.Pending is { } value && !Equal(field.Source(_baseline), value)).ToArray();
        if (changes.Length == 0) return;
        var current = _configuration.Sections.GetValueOrDefault(Id);
        if (current?.Version != _baseline?.Version || changes.Any(field => !Equal(field.Source(current), field.Source(_baseline))))
            throw new EditorOperationException("settings.changed", "Configuration fields changed. Cancel the draft and retry.");
        candidate.Patch(_section, changes.Where(field => field.Pending!.Value.Json is not null).ToDictionary(field => field.Descriptor.Id, field => field.Pending!.Value.Json!.Value),
            changes.Where(field => !field.Pending!.Value.IsProvided).Select(field => field.Descriptor.Id).ToArray());
    }
    public static bool Equal(ProjectSectionData? left, ProjectSectionData? right) => left is null ? right is null
        : right is not null && left.Version == right.Version && JsonElement.DeepEquals(left.Data, right.Data);
    private static bool Equal(FieldValue left, FieldValue right) => left.Json is { } a ? right.Json is { } b && JsonElement.DeepEquals(a, b) : right.Json is null;
    public void Dispose() { _disposed = true; foreach (var field in _fields.Values) field.Dispose(); }

    private sealed class Field : IFieldBinding, IDisposable
    {
        private readonly ConfigurationSectionEditor _owner;
        private readonly bool _canEdit;
        private IReadOnlyList<FieldEditorOption>? _options;
        public FieldDescriptor Descriptor { get; }
        public FieldValue? Pending { get; private set; }
        public FieldDraftState Draft { get; }
        public object? HostContext => _owner;
        public bool IsEnabled => _canEdit && !_owner._disposed && _owner._isCurrent();
        public bool IsVisible => Descriptor.Id != "defaultOrientation" || _owner.ShowDefaultOrientation;
        public object? Label => Descriptor.LabelKey ?? Descriptor.Id;
        public IReadOnlyList<FieldEditorOption>? Options => _options;
        public event EventHandler? Changed;
        public Field(ConfigurationSectionEditor owner, FieldDescriptor descriptor, bool canEdit)
        {
            _owner = owner; Descriptor = descriptor; _canEdit = canEdit; _options = owner.Options(descriptor);
            Draft = new(() => Source(owner._configuration.Sections.GetValueOrDefault(owner.Id)).Json?.GetRawText() ?? "<absent>");
            Draft.Changed += owner._changed;
        }
        public FieldValue Source(ProjectSectionData? section) => section is not null && section.Data.TryGetProperty(Descriptor.Id, out var value) ? new(value) : default;
        public FieldValue Read() => Pending ?? (Source(_owner._baseline) is { IsProvided: true } source ? source
            : _owner._resolved.TryGetProperty(Descriptor.Id, out var value) ? new(value) : default);
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsEnabled) return Task.FromResult<ValueIssue?>(new("settings.obsolete", "Configuration field is unavailable."));
            Pending = value; _owner._changed(); Changed?.Invoke(this, EventArgs.Empty);
            if (Descriptor.Id == "audioBuses") _owner.AudioBusesChanged?.Invoke();
            if (Descriptor.Id == "orientationPolicy") _owner.RefreshDefaultOrientationVisibility();
            return Task.FromResult<ValueIssue?>(null);
        }
        public void RefreshLocalization()
        {
            _options = _owner.Options(Descriptor);
            NotifyChanged();
        }
        public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
        public void Dispose() { Draft.Changed -= _owner._changed; Changed = null; }
    }
}
