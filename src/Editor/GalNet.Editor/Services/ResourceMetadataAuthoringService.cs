using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Contracts.Builtins;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Dock;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

public sealed record ResourceMetadataDraft(string Owner, bool Group, string ResourceId, string Culture,
    JsonElement Metadata, LocalizationFileSnapshot? File, string Name, int? UnlockId, string? PreviewResourceId);
public sealed record ResourceCoverOption(string? Id, string Label) { public override string ToString() => Label; }

/// <summary>Resource identity, default-language text and Gallery membership share one undoable edit.</summary>
public sealed class ResourceMetadataAuthoringService(LocalizationDocumentStore store, ProjectLocalizationQuery query,
    IProjectContentSession content, IEditorDocumentService documents, IProjectService projects, IResourceTypeCatalog types,
    LocalizationAuthoringService authoring, GameTextAuthoringService texts, IEditorLocalizationService text) : IDisposable
{
    private readonly Dictionary<(string, bool), ResourceMetadataEditorViewModel> _editors = [];
    public ResourceMetadataEditorViewModel Open(string owner, bool group)
    {
        if (!_editors.TryGetValue((owner, group), out var editor)) _editors.Add((owner, group), editor = new(this, owner, group));
        return editor;
    }
    public void Dispose() { foreach (var editor in _editors.Values) editor.Dispose(); _editors.Clear(); }
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public IProjectContentSession Content => content;
    public IEditorLocalizationService Text => text;
    public IReadOnlyList<ResourceCoverOption> Covers()
    {
        var assets = projects.Current!.Services.GetRequiredService<IAssetCatalogService>();
        var result = new List<ResourceCoverOption> { new(null, "—") }; var pending = new Stack<string>(); pending.Push("");
        while (pending.TryPop(out var folder)) foreach (var asset in assets.GetDirectory(folder))
        {
            if (asset.IsDirectory) { if ((File.GetAttributes(asset.FullPath) & FileAttributes.ReparsePoint) == 0) pending.Push(asset.RelativePath); }
            else if (asset.IsImage && asset.Id is not null) result.Add(new(asset.Id, asset.RelativePath));
        }
        return result.OrderBy(option => option.Id is null ? 0 : 1).ThenBy(option => option.Label, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public ResourceMetadataDraft Capture(string owner, bool group)
    {
        var culture = content.CaptureWorking().Configuration.ReadDefaultOptions().TargetLocale.Code;
        LocalizationFileSnapshot? file = null;
        JsonElement metadata;
        string resourceId;
        if (group)
        {
            var node = documents.CurrentDocument.Nodes.Single(node => node.Id == owner && node.Type == "Group");
            metadata = JsonSerializer.SerializeToElement(node.Metadata, Json); resourceId = StoryResourceIds.Group(owner);
        }
        else
        {
            file = store.CaptureResourceMetadata(owner + ".meta");
            if (!file.Exists) throw new InvalidOperationException(text["Resource.MetadataMissing"]);
            var meta = new AssetMetaCodec(types).Deserialize(LocalizationJson.Decode(file.Data), owner[7..]);
            resourceId = meta.Id; using var json = JsonDocument.Parse(LocalizationJson.Decode(file.Data)); metadata = json.RootElement.Clone();
        }
        var key = metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("displayNameKey", out var name) ? name.GetString() : null;
        var id = metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("galleryUnlockId", out var unlock) && unlock.ValueKind != JsonValueKind.Null ? unlock.GetInt32() : (int?)null;
        return new(owner, group, resourceId, culture, metadata, file, string.IsNullOrWhiteSpace(key) ? "" : query.Get()?.FindDirect(culture, key)?.Text ?? "", id, metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("galleryPreviewAssetId", out var preview) ? preview.GetString() : null);
    }
    private void Check(ResourceMetadataDraft expected)
    {
        var current = Capture(expected.Owner, expected.Group);
        if (current.Culture != expected.Culture || current.Name != expected.Name || !JsonElement.DeepEquals(current.Metadata, expected.Metadata))
            throw new EditorOperationException("resource.changed", text["Resource.Changed"]);
    }
    public async Task SetAsync(ResourceMetadataDraft expected, string name, int? unlockId, string? previewAssetId = null)
    {
        await _gate.WaitAsync();
        try
        {
            Check(expected);
            if (unlockId is <= 0) throw new InvalidDataException(text["Resource.InvalidGalleryId"]);
            if (unlockId is { } id) ValidateUnique(expected, id);
            if (name == expected.Name && unlockId == expected.UnlockId && (previewAssetId is null || previewAssetId == (expected.PreviewResourceId ?? ""))) return;
            if (expected.File is { } file) { await content.CommitFileDraftsAsync(file.Id); Check(expected); }
            var root = expected.Metadata.ValueKind == JsonValueKind.Object ? JsonNode.Parse(expected.Metadata.GetRawText())!.AsObject() : JsonSerializer.SerializeToNode(new AssetMeta { Id = expected.ResourceId, TypeId = "group", Path = expected.Owner }, Json)!.AsObject();
            var key = root["displayNameKey"]?.GetValue<string>();
            var patches = new List<LocalizationFilePatch>();
            if (!string.IsNullOrWhiteSpace(name))
            {
                if (key is not null && name != expected.Name && query.Get()!.References.Count(reference => reference.Key == key) > 1)
                    key = "asset." + Guid.NewGuid().ToString("N") + ".name";
                key ??= "asset." + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(expected.ResourceId))) + ".name";
                var target = store.CaptureText(key, expected.Culture, $"I18n/{expected.Culture}/resources.json");
                patches.AddRange(await texts.PrepareTextAsync(target, name, [], () => Check(expected)));
                root["displayNameKey"] = key;
            }
            else root.Remove("displayNameKey");
            if (unlockId is { } value) root["galleryUnlockId"] = value; else root.Remove("galleryUnlockId");
            if (previewAssetId is not null)
            {
                if (previewAssetId.Length > 0 && !Covers().Any(option => option.Id == previewAssetId)) throw new InvalidDataException(text["Resource.InvalidCover"]);
                if (previewAssetId.Length == 0) root.Remove("galleryPreviewAssetId"); else root["galleryPreviewAssetId"] = previewAssetId;
            }
            Check(expected);
            var groups = new List<LocalizationGroupMetadataPatch>();
            if (expected.Group) groups.Add(new(expected.Owner, expected.Metadata, JsonSerializer.SerializeToElement(root, Json)));
            else patches.Add(new(expected.File!, LocalizationJson.WriteLike(expected.File!.Data, root)));
            await authoring.ApplyAsync(store.PrepareResource(patches, groups), expected.Owner, "History.ResourceMetadata");
        }
        finally { _gate.Release(); }
    }
    public async Task SetImportOptionsAsync(string owner, string filter, string compression)
    {
        // An explicit import-option change also commits pending name input before rebasing the sidecar.
        if (_editors.TryGetValue((owner, false), out var editor)) await editor.CommitAsync();
        await _gate.WaitAsync();
        try
        {
            var file = store.CaptureResourceMetadata(owner + ".meta");
            await content.CommitFileDraftsAsync(file.Id);
            file = store.CaptureResourceMetadata(owner + ".meta");
            var root = JsonNode.Parse(LocalizationJson.Decode(file.Data))!.AsObject();
            var meta = new AssetMetaCodec(types).Deserialize(LocalizationJson.Decode(file.Data), owner[7..]);
            if (meta is GalNet.Contracts.Builtins.Assets.SpriteAssetMeta) root["filter"] = filter;
            root["compress"] = compression;
            await authoring.ApplyAsync(store.PrepareResource([new(file, LocalizationJson.WriteLike(file.Data, root))], []), owner, "History.ResourceMetadata");
        }
        finally { _gate.Release(); }
    }
    private void ValidateUnique(ResourceMetadataDraft expected, int id)
    {
        var working = content.CaptureWorking();
        if (working.Graph.Nodes.Any(node => node.Type == "Group" && !(expected.Group && node.Id == expected.Owner) && node.Metadata?.GalleryUnlockId == id)) throw new InvalidDataException(text.Format("Resource.DuplicateGalleryId", id));
        var reader = new ProjectRawReader(projects.Current!.RootPath);
        var managed = working.Files.Values.Where(file => file.Path.StartsWith("Assets/", StringComparison.Ordinal) && file.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).GroupBy(file => file.Path, ProjectFilePaths.Comparer).ToDictionary(group => group.Key, group => group.FirstOrDefault(file => file.Exists) ?? group.First(), ProjectFilePaths.Comparer);
        var paths = Directory.Exists(reader.Resolve("Assets")) ? reader.Files("Assets", []).Where(path => path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) : [];
        foreach (var path in paths.Concat(managed.Keys).Distinct(ProjectFilePaths.Comparer))
        {
            if (!expected.Group && ProjectFilePaths.Comparer.Equals(path, expected.Owner + ".meta")) continue;
            if (managed.TryGetValue(path, out var file) && !file.Exists) continue;
            var bytes = file is { ManagedContent: true } ? file.Data : File.ReadAllBytes(reader.Resolve(path));
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.TryGetProperty("galleryUnlockId", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == id) throw new InvalidDataException(text.Format("Resource.DuplicateGalleryId", id));
        }
    }
    public void OpenTranslations()
    {
        var services = projects.Current!.Services;
        services.GetRequiredService<EditorDockFactory>().OpenPanel(EditorDockPanelIds.Localization);
        var panel = services.GetRequiredService<GameLocalizationPanelViewModel>();
        panel.Selected = panel.Categories.FirstOrDefault(category => category.Id == "resources");
    }
}
