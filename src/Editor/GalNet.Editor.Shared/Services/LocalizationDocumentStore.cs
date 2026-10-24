using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Shared.Services;

public sealed record LocalizationFileSnapshot(string Id, string Path, bool Exists, byte[] Data);
public sealed record LocalizationTextTarget(string Key, string Culture, LocalizationFileSnapshot File, string? Text);
public sealed record LocalizationFilePatch(LocalizationFileSnapshot Before, byte[] After);

/// <summary>Managed author documents, not a second writable translation database.</summary>
public sealed partial class LocalizationDocumentStore(IProjectContentSession content, IEditorDocumentService documents,
    IProjectService projects, ProjectLocalizationQuery query) : IDisposable
{
    private bool _disposed;
    private string? _projectId;
    internal void RequireActive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!documents.IsLoaded || projects.Current is not { } project || content.RepairRequired)
            throw new InvalidOperationException("Localization authoring is unavailable.");
        _projectId ??= project.Id;
        if (_projectId != project.Id) throw new InvalidOperationException("Localization authoring belongs to a different project.");
    }

    public LocalizationFileSnapshot CaptureFile(string path)
    {
        RequireActive();
        path = LocalizationJson.Path(path);
        var live = content.FileDocuments.Values.Where(file => file.Exists && ProjectFilePaths.Comparer.Equals(file.Path, path)).ToArray();
        if (live.Length > 1) throw ProjectFilePaths.Error("changed", path);
        if (live.SingleOrDefault() is { ManagedContent: true } managed)
            return new(managed.Id, path, true, managed.Data.ToArray());
        var full = new ProjectRawReader(projects.Current!.RootPath).Resolve(path);
        // A pending deletion masks the old disk file. A new identity may subsequently occupy this path.
        if (live.Length == 0 && content.FileDocuments.Values.Any(file => !file.Exists && ProjectFilePaths.Comparer.Equals(file.Path, path)))
            return new(Guid.NewGuid().ToString("N"), path, false, LocalizationJson.Initial(path));
        if (!File.Exists(full)) return new(Guid.NewGuid().ToString("N"), path, false, LocalizationJson.Initial(path));
        var bytes = File.ReadAllBytes(full);
        var file = content.TrackLocalizationFile(path, bytes);
        return new(file.Id, path, true, file.Data.ToArray());
    }

    public LocalizationTextTarget CaptureText(string key, string? culture = null, string? newFile = null)
    {
        RequireActive();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        culture = System.Globalization.CultureInfo.GetCultureInfo(culture ?? content.CaptureWorking().Settings.TargetLocale.Code).Name;
        var source = query.Get()?.FindDirect(culture, key);
        var path = source?.File ?? newFile ?? $"I18n/{culture}/authoring.json";
        path = LocalizationJson.Path(path);
        if (!path.StartsWith($"I18n/{culture}/", StringComparison.OrdinalIgnoreCase)) throw ProjectFilePaths.Error("category", path);
        var file = CaptureFile(path);
        LocalizationJson.Validate(path, file.Data);
        return new(key, culture, file, LocalizationJson.Text(file.Data, key));
    }

    public LocalizationFilePatch PrepareText(LocalizationTextTarget target, string text)
    {
        RequireActive();
        var current = CaptureText(target.Key, target.Culture, target.File.Path);
        if (current.Text != target.Text || current.File.Exists != target.File.Exists ||
            !ProjectFilePaths.Comparer.Equals(current.File.Path, target.File.Path) || current.File.Exists && current.File.Id != target.File.Id)
            throw ProjectFilePaths.Error("changed", target.File.Path);
        // New files retain the identity allocated when this draft was created.
        var before = current.File with { Id = target.File.Id };
        return new(before, current.Text == text ? before.Data.ToArray() : LocalizationJson.SetText(before.Data, target.Key, text));
    }

    public IReadOnlyDictionary<string, LocalizationTextTarget> CaptureTexts(IEnumerable<string> keys, string culture)
    {
        RequireActive(); culture = System.Globalization.CultureInfo.GetCultureInfo(culture).Name;
        var index = query.Get()!;
        var files = new Dictionary<string, (LocalizationFileSnapshot File, IReadOnlyDictionary<string, string> Values)>(ProjectFilePaths.Comparer);
        var result = new Dictionary<string, LocalizationTextTarget>(StringComparer.Ordinal);
        foreach (var key in keys.Distinct(StringComparer.Ordinal))
        {
            var path = index.FindDirect(culture, key)?.File ?? $"I18n/{culture}/authoring.json";
            if (!files.TryGetValue(path, out var item))
            {
                var file = CaptureFile(path); LocalizationJson.Validate(path, file.Data);
                files.Add(path, item = (file, LocalizationJson.Texts(file.Data)));
            }
            result.Add(key, new(key, culture, item.File, item.Values.GetValueOrDefault(key)));
        }
        return result;
    }
    public IReadOnlyList<LocalizationFilePatch> PrepareTexts(IEnumerable<LocalizationTextTarget> targets, IReadOnlyDictionary<string, string> values)
    {
        var expected = targets.ToArray(); if (expected.Length == 0) return [];
        var current = CaptureTexts(expected.Select(target => target.Key), expected[0].Culture);
        foreach (var target in expected)
        {
            var now = current[target.Key];
            if (now.Text != target.Text || now.File.Exists != target.File.Exists || !ProjectFilePaths.Comparer.Equals(now.File.Path, target.File.Path) || now.File.Exists && now.File.Id != target.File.Id)
                throw ProjectFilePaths.Error("changed", target.File.Path);
        }
        return current.Values.GroupBy(target => target.File.Path, ProjectFilePaths.Comparer).Select(group => new LocalizationFilePatch(group.First().File,
            LocalizationJson.SetTexts(group.First().File.Data, group.ToDictionary(target => target.Key, target => values[target.Key], StringComparer.Ordinal)))).ToArray();
    }

    public LocalizationEditOperation Prepare(params LocalizationFilePatch[] patches)
    {
        RequireActive();
        return new(content, RequireActive, patches);
    }
    public LocalizationEditOperation PrepareWithEntries(IEnumerable<LocalizationFilePatch> patches, IEnumerable<LocalizationEntryPatch> entries)
    {
        RequireActive();
        return new(content, RequireActive, patches, documents, entries);
    }
    public LocalizationEditOperation PrepareWithOptions(IEnumerable<LocalizationFilePatch> patches, IEnumerable<LocalizationOptionPatch> options)
    {
        RequireActive();
        return new(content, RequireActive, patches, documents, options: options);
    }
    public void Dispose() => _disposed = true;
}
