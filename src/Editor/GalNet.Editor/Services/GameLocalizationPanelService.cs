using System.Globalization;
using System.Text.Json.Nodes;
using GalNet.Core.I18n;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

public sealed record LocalizationCategory(string Id, string Name);
public sealed record LocalizationRowContext(string Title, string Slot, string Details, LocalizationReference Reference)
{
    public string DisplayDetails { get; init; } = Details;
}
public sealed record LocalizationTableRow(string Key, string Context, IReadOnlyList<LocalizationReference> References)
{
    public IReadOnlyList<LocalizationRowContext> Locations { get; init; } = [];
}
public sealed record LocalizationTableSnapshot(string Source, IReadOnlyList<string> Languages,
    IReadOnlyList<LocalizationCategory> Categories, IReadOnlyList<LocalizationTableRow> Rows, LocalizationReferenceIndex Index, byte[] Metadata);
public sealed record LocalizationCellDraft(string Category, LocalizationTextTarget Target, string SourceLocale, string? SourceText);
public sealed record DevelopmentLanguagePreview(string Before, string Target, int Missing, string Evidence);

/// <summary>Projects the existing author documents. Cell values and language order share project history.</summary>
public sealed class GameLocalizationPanelService(IProjectContentSession content, IProjectService projects,
    LocalizationDocumentStore store, ProjectLocalizationQuery query, GameTextAuthoringService gameTexts,
    LocalizationAuthoringService authoring, EditorHistories histories, IEditorLocalizationService text)
{
    public IEditorLocalizationService Text => text;
    public IProjectContentSession Content => content;
    public void Navigate(LocalizationTableRow row)
    {
        Active();
        if (row.References.FirstOrDefault() is { } reference)
            projects.Current!.Services.GetRequiredService<GalNet.Editor.Dock.EditorDockFactory>().NavigateLocalization(reference.Owner, reference.EntryId);
    }
    public void Navigate(LocalizationReference reference)
    {
        Active();
        projects.Current!.Services.GetRequiredService<GalNet.Editor.Dock.EditorDockFactory>().NavigateLocalization(reference.Owner, reference.EntryId);
    }
    private string Source => content.CaptureWorking().Settings.TargetLocale.Code;
    public IReadOnlyList<string> Languages => new[] { Source }.Concat(content.CaptureWorking().Settings.AvailableLocales.Select(locale => locale.Code))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private void Active() { _ = store.CaptureFile(LocalizationJson.MetadataPath); }
    private static string Normalize(string value)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(value.Trim().Replace('_', '-')).Name;
            if (culture.Length > 0) return culture;
        }
        catch (CultureNotFoundException) { }
        throw new EditorOperationException("i18n.language", "Invalid language.");
    }
    private IReadOnlyList<LocalizationReference> References(LocalizationReferenceIndex index, string category)
    {
        var branches = category == "branches" ? content.CaptureWorking().Graph.Nodes.Where(node => node.Type == "Branch").Select(node => node.Id).ToHashSet(StringComparer.Ordinal) : [];
        return index.References.Where(reference => reference.Semantic == FieldValueSemantic.LocalizedText &&
            (category == "branches" ? branches.Contains(reference.Owner) : reference.Owner == category)).ToArray();
    }
    public LocalizationTableSnapshot Snapshot(string category)
    {
        Active(); var document = content.CaptureWorking(); var index = query.Get()!; var source = document.Settings.TargetLocale.Code;
        var categories = new List<LocalizationCategory> { new("characters", text["I18n.Characters"]) };
        categories.AddRange(document.Graph.Nodes.Where(node => node.Type == "Group").Select(node => new LocalizationCategory(node.Id, node.Name)));
        categories.Add(new("branches", text["I18n.Branches"]));
        LocalizationRowContext Context(LocalizationReference reference)
        {
            var id = reference.EntryId ?? reference.Owner;
            var title = id.Length > 12 ? id[..8] + "…" + id[^4..] : id;
            var slot = reference.JsonPointer.TrimStart('/');
            var details = reference.Owner + "/" + id + reference.JsonPointer;
            var displayDetails = details;
            if (category == "characters")
            {
                var character = index.Characters.FirstOrDefault(item => item.Id == reference.EntryId);
                title = character is null ? id : index.FindDirect(source, character.DisplayNameKey)?.Text ?? id;
                slot = "";
            }
            else if (category == "branches")
            {
                var node = document.Graph.Nodes.FirstOrDefault(item => item.Id == reference.Owner);
                title = node?.Name ?? reference.Owner;
                slot = id + " · " + slot;
            }
            else
            {
                var entry = reference.EntryId is { } entryId
                    ? document.GroupEntries.GetValueOrDefault(reference.Owner)?.FirstOrDefault(item => item.StableId == entryId)
                    : null;
                if (entry is not null)
                {
                    title = $"Entry{entry.Id}";
                    displayDetails = $"Entry{entry.Id}{reference.JsonPointer}";
                }
            }
            return new(title, slot, details, reference) { DisplayDetails = displayDetails };
        }
        var rows = References(index, category).GroupBy(reference => reference.Key, StringComparer.Ordinal).Select(group =>
        {
            var refs = group.ToArray(); var locations = refs.Select(Context).ToArray();
            var context = string.Join(" · ", locations.Select(location => location.Title + " " + location.Slot + " " + location.DisplayDetails));
            return new LocalizationTableRow(group.Key, context, refs) { Locations = locations };
        }).ToArray();
        return new(source, Languages, categories, rows, index, store.CaptureFile(LocalizationJson.MetadataPath).Data);
    }
    private void Current(LocalizationCellDraft draft)
    {
        Active(); var index = query.Get()!;
        if (Source != draft.SourceLocale || !Languages.Contains(draft.Target.Culture, StringComparer.OrdinalIgnoreCase) ||
            !References(index, draft.Category).Any(reference => reference.Key == draft.Target.Key) ||
            index.FindDirect(Source, draft.Target.Key)?.Text != draft.SourceText)
            throw new EditorOperationException("i18n.stale", text["I18n.Stale"]);
    }
    public LocalizationCellDraft Capture(string category, string key, string culture)
    {
        var draft = new LocalizationCellDraft(category, store.CaptureText(key, culture), Source, query.Get()!.FindDirect(Source, key)?.Text);
        Current(draft); return draft;
    }
    public async Task SetAsync(LocalizationCellDraft draft, string value)
    {
        Current(draft);
        var metadata = store.CaptureFile(LocalizationJson.MetadataPath);
        foreach (var file in new[] { draft.Target.File, metadata }.Where(file => file.Exists).DistinctBy(file => file.Id))
        { await content.CommitFileDraftsAsync(file.Id); Current(draft); }
        if (draft.Target.Culture == Source)
        {
            var patches = await gameTexts.PrepareTextAsync(draft.Target, value, [], () => Current(draft));
            Current(draft); await ApplyCellAsync(draft, patches.ToArray()); return;
        }
        var target = draft.Target;
        // Empty saved baselines remain safe after undo/save/redo, just as normal source authoring does.
        var missing = new[] { target.File, metadata }.Where(file => !file.Exists).DistinctBy(file => file.Path).ToArray();
        if (missing.Length > 0)
        { await store.Prepare(missing.Select(file => new LocalizationFilePatch(file, file.Data)).ToArray()).ApplyAsync(true); Current(draft); }
        if (!target.File.Exists) target = target with { File = store.CaptureFile(target.File.Path) };
        var translation = store.PrepareText(target, value);
        var meta = store.CaptureFile(LocalizationJson.MetadataPath); var root = JsonNode.Parse(LocalizationJson.Decode(meta.Data))!;
        root["textStates"] ??= new JsonObject();
        var states = root["textStates"]!;
        states[target.Key] ??= new JsonObject { ["sourceLocale"] = Source, ["translations"] = new JsonObject() };
        var state = states[target.Key]!;
        if (state["sourceLocale"]?.GetValue<string>() != Source) state["translations"] = new JsonObject();
        state["sourceLocale"] = Source;
        if (draft.SourceText is { } source) state["sourceFingerprint"] = LocalizationTextState.Fingerprint(source);
        else state["sourceFingerprint"] ??= LocalizationTextState.Fingerprint("");
        state["translations"]![target.Culture] = draft.SourceText is { } original ? JsonValue.Create(LocalizationTextState.Fingerprint(original)) : null;
        Current(draft);
        await ApplyCellAsync(draft, [translation, new(meta, LocalizationJson.WriteLike(meta.Data, root))]);
    }
    private Task ApplyCellAsync(LocalizationCellDraft draft, LocalizationFilePatch[] patches) => histories.Content.ExecuteAsync(
        new GuardedCellEdit(authoring.CreateEdit(store.Prepare(patches), draft.Target.Culture, "History.GameText"), () => Current(draft)));
    private sealed record GuardedCellEdit(IAsyncUndoableEdit Edit, Action Check) : IAsyncUndoableEdit
    {
        public string Description => Edit.Description;
        public EditorText DisplayText => Edit.DisplayText;
        public long RetainedBytes => Edit.RetainedBytes;
        public ValueTask RedoAsync() { Check(); return Edit.RedoAsync(); }
        public ValueTask UndoAsync() => Edit.UndoAsync();
    }
    public Task AddLanguageAsync(string value)
    {
        Active(); var code = Normalize(value);
        if (Languages.Contains(code, StringComparer.OrdinalIgnoreCase)) throw new EditorOperationException("i18n.duplicate", text["I18n.Duplicate"]);
        return SetLanguagesAsync(Source, Languages.Append(code).ToArray());
    }
    public Task MoveLanguageAsync(string code, int direction)
    {
        Active(); var languages = Languages.ToArray(); var position = Array.IndexOf(languages, code); var next = position + Math.Sign(direction);
        if (position <= 0 || next <= 0 || next >= languages.Length) return Task.CompletedTask;
        (languages[position], languages[next]) = (languages[next], languages[position]);
        return SetLanguagesAsync(Source, languages);
    }
    public Task RemoveLanguageAsync(string value)
    {
        Active(); var code = Normalize(value);
        if (code == Source) throw new EditorOperationException("i18n.source", text["Uiux.I18n.RemoveSource"]);
        if (!Languages.Contains(code, StringComparer.OrdinalIgnoreCase)) throw new EditorOperationException("i18n.stale", text["I18n.Stale"]);
        // Only remove support from settings. Retain translations and evidence for re-adding/undo.
        return SetLanguagesAsync(Source, Languages.Where(language => !language.Equals(code, StringComparison.OrdinalIgnoreCase)).ToArray());
    }
    public DevelopmentLanguagePreview Preview(string value)
    {
        Active(); var target = Normalize(value);
        if (!Languages.Contains(target, StringComparer.OrdinalIgnoreCase)) throw new EditorOperationException("i18n.language", text["I18n.AddFirst"]);
        var index = query.Get()!; var keys = index.References.Where(reference => reference.Semantic == FieldValueSemantic.LocalizedText).Select(reference => reference.Key).Distinct(StringComparer.Ordinal).ToArray();
        return new(Source, target, keys.Count(key => index.FindDirect(target, key) is null),
            string.Join("|", keys.Order(StringComparer.Ordinal).Select(key => key.Length + ":" + key + ":" + (index.FindDirect(target, key)?.Text is { } value ? LocalizationTextState.Fingerprint(value) : "missing"))));
    }
    public Task ChangeDevelopmentLanguageAsync(DevelopmentLanguagePreview preview)
    {
        void CurrentPreview() { if (Preview(preview.Target) != preview) throw new EditorOperationException("i18n.stale", text["I18n.Stale"]); }
        CurrentPreview();
        return SetLanguagesAsync(preview.Target, new[] { preview.Target }.Concat(Languages).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), CurrentPreview);
    }
    private Task SetLanguagesAsync(string source, IReadOnlyList<string> languages, Action? validate = null)
    {
        Active(); var settings = projects.Current!.Settings;
        return histories.Content.ExecuteAsync(new LanguageEdit(settings, settings.TargetLocale.Code, settings.AvailableLocales.Select(locale => locale.Code).ToArray(), source, languages.ToArray(), Active, validate));
    }
    private sealed record LanguageEdit(GalNet.Core.Settings.ProjectSettings Settings, string BeforeSource, string[] Before, string AfterSource, string[] After, Action Active, Action? Validate) : IUndoableEdit
    {
        public string Description => "Change project languages";
        public EditorText DisplayText => new("History.Languages");
        public long RetainedBytes => (Before.Sum(value => value.Length) + After.Sum(value => value.Length)) * 2L;
        public void Undo() => Apply(AfterSource, After, BeforeSource, Before);
        public void Redo() { Validate?.Invoke(); Apply(BeforeSource, Before, AfterSource, After); }
        private void Apply(string expectedSource, string[] expected, string source, string[] value)
        {
            Active();
            if (Settings.TargetLocale.Code != expectedSource || !Settings.AvailableLocales.Select(locale => locale.Code).SequenceEqual(expected))
                throw new EditorOperationException("i18n.stale", "Project languages changed.");
            Settings.TargetLocale = new I18nLocale(source); Settings.AvailableLocales = value.Select(code => new I18nLocale(code)).ToList();
        }
    }
}
