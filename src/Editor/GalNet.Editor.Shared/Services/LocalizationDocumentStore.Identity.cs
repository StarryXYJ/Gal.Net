using System.Text.Json.Nodes;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

public sealed partial class LocalizationDocumentStore
{
    /// <summary>Project identity is durable author metadata, independent of folder/name and content undo.</summary>
    public async Task<string> EnsureExchangeIdentityAsync()
    {
        RequireActive(); var file = CaptureFile(LocalizationJson.MetadataPath);
        LocalizationJson.Validate(file.Path, file.Data);
        if (file.Exists) await content.CommitFileDraftsAsync(file.Id);
        string identity = "";
        await content.PublishStructureAsync((working, saved) =>
        {
            RequireActive(); var current = Current(working, file); var baseline = Current(saved, file);
            LocalizationJson.Validate(file.Path, current); LocalizationJson.Validate(file.Path, baseline);
            identity = JsonNode.Parse(LocalizationJson.Decode(current))!["exchangeProjectId"]?.GetValue<string>() ??
                JsonNode.Parse(LocalizationJson.Decode(baseline))!["exchangeProjectId"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
            var plan = new ProjectFilePlan(); if (!file.Exists) plan.ExpectedHashes[file.Path] = null;
            return new ExchangeIdentity(file, identity, plan);
        });
        return identity;
    }
    private sealed record ExchangeIdentity(LocalizationFileSnapshot Before, string Identity, ProjectFilePlan Files) : IProjectFileStructureChange
    {
        public void Apply(EditorProjectDocument document, AuthorDocumentVersion version)
        {
            var bytes = Current(document, Before); var root = JsonNode.Parse(LocalizationJson.Decode(bytes))!;
            if (root["exchangeProjectId"]?.GetValue<string>() is { } current && current != Identity) throw new InvalidDataException("Project identity changed.");
            root["exchangeProjectId"] = Identity;
            if (!document.Files.TryGetValue(Before.Id, out var file)) document.Files[Before.Id] = file = new() { Id = Before.Id, Path = Before.Path, ManagedContent = true };
            file.Exists = true; file.Data = LocalizationJson.WriteLike(bytes, root);
        }
    }
}
