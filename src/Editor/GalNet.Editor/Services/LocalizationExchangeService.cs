using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

public sealed record LocalizationImportCandidate(LocalizationExchangeRow Row, bool Known, bool SourceChanged, LocalizationTextTarget? Target, string? CurrentSource);
public sealed record LocalizationImportPreview(GalProject Project, string Culture, string SourceLocale, string Evidence, LocalizationImportCandidate[] Rows);

public sealed class LocalizationExchangeService(IProjectService projects, IProjectContentSession content, ProjectLocalizationQuery query,
    LocalizationDocumentStore store, LocalizationAuthoringService authoring, EditorHistories histories)
{
    private GalProject Active()
    { _ = store.CaptureFile(LocalizationJson.MetadataPath); return projects.Current!; }
    private string Source => content.CaptureWorking().Settings.TargetLocale.Code;
    private string? Identity => JsonNode.Parse(LocalizationJson.Decode(store.CaptureFile(LocalizationJson.MetadataPath).Data))!["exchangeProjectId"]?.GetValue<string>() is { } identity ? Guid.Parse(identity).ToString("N") : null;
    private string Evidence()
    {
        Active(); var index = query.Get()!; var meta = JsonNode.Parse(LocalizationJson.Decode(store.CaptureFile(LocalizationJson.MetadataPath).Data))!.AsObject();
        meta.Remove("characterHighWater");
        return LocalizationTextState.Fingerprint(JsonSerializer.Serialize(new { Source, Languages = content.CaptureWorking().Settings.AvailableLocales.Select(locale => locale.Code), index.References, index.Texts, Metadata = meta }));
    }
    public async Task<string> ExportAsync(string culture, string? category, bool runtime)
    {
        Active(); culture = LocalizationExchange.Culture(culture);
        await content.CommitDraftsAsync(); Active();
        if (!content.CaptureWorking().Settings.AvailableLocales.Any(locale => locale.Code == culture)) throw new EditorOperationException("exchange.language", "Language is not supported.");
        var identity = runtime ? "" : await store.EnsureExchangeIdentityAsync(); Active();
        var snapshot = content.CaptureWorking(); var index = query.Get()!; var sourceLocale = snapshot.Settings.TargetLocale.Code;
        var branches = snapshot.Graph.Nodes.Where(node => node.Type == "Branch").Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var references = index.References.Where(reference => reference.Semantic == FieldValueSemantic.LocalizedText &&
            (category is null || (category == "branches" ? branches.Contains(reference.Owner) : reference.Owner == category)));
        var rows = references.GroupBy(reference => reference.Key, StringComparer.Ordinal).Select(group =>
        {
            var source = index.FindDirect(sourceLocale, group.Key)?.Text;
            return new LocalizationExchangeRow(group.Key, source, source is null ? null : LocalizationTextState.Fingerprint(source),
                group.Select(reference => $"{reference.Owner}/{reference.EntryId}{reference.JsonPointer}").ToArray(), index.FindDirect(culture, group.Key)?.Text);
        }).ToArray();
        return runtime ? JsonSerializer.Serialize(rows.Where(row => row.Translation is not null).ToDictionary(row => row.Key, row => row.Translation), new JsonSerializerOptions { WriteIndented = true })
            : LocalizationExchange.Write(new(1, identity, sourceLocale, culture, rows, index.Complete));
    }
    public LocalizationImportPreview Preview(string json, string flatLanguage)
    {
        var project = Active(); var input = LocalizationExchange.Parse(json, flatLanguage);
        if (input.ProjectId is not null && (input.ProjectId != Identity || input.SourceLocale != Source)) throw new EditorOperationException("exchange.project", "Template project or source language does not match.");
        if (input.TargetLocale == Source || !content.CaptureWorking().Settings.AvailableLocales.Any(locale => locale.Code == input.TargetLocale))
            throw new EditorOperationException("exchange.language", "Import requires a supported translation language other than the development language.");
        var index = query.Get()!; var sourceLocale = Source;
        var known = index.References.Where(reference => reference.Semantic == FieldValueSemantic.LocalizedText).Select(reference => reference.Key).ToHashSet(StringComparer.Ordinal);
        var targets = store.CaptureTexts(input.Rows.Where(row => known.Contains(row.Key)).Select(row => row.Key), input.TargetLocale);
        var rows = input.Rows.Select(row => new LocalizationImportCandidate(row, known.Contains(row.Key),
            row.SourceFingerprint is not null && !string.Equals(row.SourceFingerprint, index.FindDirect(sourceLocale, row.Key)?.Text is { } source ? LocalizationTextState.Fingerprint(source) : null, StringComparison.OrdinalIgnoreCase),
            targets.GetValueOrDefault(row.Key), index.FindDirect(sourceLocale, row.Key)?.Text)).ToArray();
        return new(project, input.TargetLocale, sourceLocale, Evidence(), rows);
    }
    private void Check(LocalizationImportPreview preview)
        => Check(preview.Project, preview.Culture, preview.SourceLocale, preview.Evidence);
    private void Check(GalProject project, string culture, string sourceLocale, string evidence)
    {
        if (!ReferenceEquals(Active(), project) || sourceLocale != Source || culture == Source || !content.CaptureWorking().Settings.AvailableLocales.Any(locale => locale.Code == culture) || Evidence() != evidence)
            throw new EditorOperationException("exchange.stale", "Import preview changed. Preview the file again.");
    }
    public async Task ApplyAsync(LocalizationImportPreview preview, IReadOnlyCollection<string> selected, bool allowEmpty)
    {
        Check(preview);
        var selection = selected.ToHashSet(StringComparer.Ordinal);
        var rows = preview.Rows.Where(row => selection.Contains(row.Row.Key)).ToArray();
        if (selection.Count != selected.Count || rows.Length != selected.Count || rows.Any(row => !row.Known || row.Row.Translation is null || row.Row.Translation.Length == 0 && !allowEmpty))
            throw new InvalidDataException("Selection contains unknown, null or disabled empty translations.");
        if (rows.Length == 0) return;
        var metadata = store.CaptureFile(LocalizationJson.MetadataPath);
        var files = rows.Select(row => row.Target!.File).Append(metadata).DistinctBy(file => file.Path).ToArray();
        foreach (var file in files.Where(file => file.Exists)) { await content.CommitFileDraftsAsync(file.Id); Active(); }
        Check(preview);
        var missing = files.Where(file => !file.Exists).ToArray();
        if (missing.Length > 0) { await store.Prepare(missing.Select(file => new LocalizationFilePatch(file, file.Data)).ToArray()).ApplyAsync(true); Check(preview); }
        var baselineFiles = new Dictionary<string, LocalizationFileSnapshot>(StringComparer.Ordinal);
        var targets = rows.Select(row =>
        {
            var target = row.Target!;
            if (target.File.Exists) return target;
            if (!baselineFiles.TryGetValue(target.File.Path, out var file)) baselineFiles[target.File.Path] = file = store.CaptureFile(target.File.Path);
            return target with { File = file };
        }).ToArray();
        var patches = store.PrepareTexts(targets, rows.ToDictionary(row => row.Row.Key, row => row.Row.Translation!, StringComparer.Ordinal)).ToDictionary(patch => patch.Before.Path, StringComparer.Ordinal);
        var meta = store.CaptureFile(LocalizationJson.MetadataPath); var root = JsonNode.Parse(LocalizationJson.Decode(meta.Data))!;
        root["textStates"] ??= new JsonObject(); var index = query.Get()!;
        foreach (var candidate in rows)
        {
            var key = candidate.Row.Key; var source = index.FindDirect(preview.SourceLocale, key)?.Text;
            var states = root["textStates"]!; states[key] ??= new JsonObject { ["translations"] = new JsonObject() };
            var state = states[key]!;
            if (state["sourceLocale"]?.GetValue<string>() != preview.SourceLocale) state["translations"] = new JsonObject();
            state["sourceLocale"] = preview.SourceLocale; state["sourceFingerprint"] = LocalizationTextState.Fingerprint(source ?? "");
            state["translations"]![preview.Culture] = source is not null && candidate.Row.SourceFingerprint is { } proof ? JsonValue.Create(proof) : null;
        }
        patches.Add(meta.Path, new(meta, LocalizationJson.WriteLike(meta.Data, root)));
        Check(preview);
        var project = preview.Project; var culture = preview.Culture; var sourceLocale = preview.SourceLocale; var evidence = preview.Evidence;
        await histories.Content.ExecuteAsync(new ImportEdit(authoring.CreateEdit(store.Prepare(patches.Values.ToArray()), culture, "History.ImportLocalization"), () => Check(project, culture, sourceLocale, evidence)));
    }
    private sealed record ImportEdit(IAsyncUndoableEdit Edit, Action Check) : IAsyncUndoableEdit
    {
        public string Description => Edit.Description;
        public EditorText DisplayText => Edit.DisplayText;
        public long RetainedBytes => Edit.RetainedBytes;
        public ValueTask RedoAsync() { Check(); return Edit.RedoAsync(); }
        public ValueTask UndoAsync() => Edit.UndoAsync();
    }
    public async Task ExportFileAsync(string path, string culture, string? category, bool runtime)
    {
        var project = Active(); var full = Path.GetFullPath(path); var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(project.RootPath));
        if (full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Export must be outside the project directory.");
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(full)!); directory is not null; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Export path traverses a directory link.");
        var json = await ExportAsync(culture, category, runtime);
        if (!ReferenceEquals(project, Active())) throw new EditorOperationException("exchange.stale", "Project closed.");
        var temporary = full + ".export-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false)); Active(); File.Move(temporary, full, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
