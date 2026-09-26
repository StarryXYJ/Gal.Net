using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalNet.Core.Variable;
using GalNet.Core.Gallery;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.ViewModels;

public partial class EditorWorkspaceViewModel
{
    private readonly IEditorDocumentService _documentService;
    private readonly IEditorSaveCoordinator _saveCoordinator;

    public IReadOnlyList<ProjectVariableDefinition> AllProjectVariableDefinitions =>
        [.. _documentService.CurrentDocument.PlayerVariables, .. _documentService.CurrentDocument.SaveVariables, .. GetGalleryVariableDefinitions()];

    public IReadOnlyList<string> GallerySystemVariableNames =>
        GalleryUnlockVariable.CreateDefinitions(GalleryCatalog.Create(_documentService.CurrentDocument.Gallery))
            .Select(definition => definition.Name)
            .ToList();

    public IReadOnlyList<ConditionVariableSuggestion> GetConditionVariableSuggestions()
    {
        return _documentService.CurrentDocument.PlayerVariables
                .Select(v => new ConditionVariableSuggestion { Name = v.Name, Scope = VariableScope.Player })
                .Concat(GallerySystemVariableNames.Select(name =>
                    new ConditionVariableSuggestion { Name = name, Scope = VariableScope.Player }))
                .Concat(_documentService.CurrentDocument.SaveVariables.Select(v => new ConditionVariableSuggestion { Name = v.Name, Scope = VariableScope.Save }))
                .ToList();
    }

    private IReadOnlyList<ProjectVariableDefinition> GetGalleryVariableDefinitions() =>
        GalleryUnlockVariable.CreateDefinitions(GalleryCatalog.Create(_documentService.CurrentDocument.Gallery))
            .Select(definition => new ProjectVariableDefinition
            {
                Name = definition.Name,
                DefaultValue = definition.DefaultValue
            })
            .ToList();

    public string BuildPreviewData()
    {
        if (_projectService.Current is not { } project)
            throw new InvalidOperationException("No project is currently open.");

        var previewPath = Path.Combine(project.TempPath, "preview");
        var document = _graphDocumentMapper.CreateDocument(
            project.Name,
            _documentService.CurrentDocument.Version,
            Nodes,
            Edges,
            _documentService.CurrentDocument.PlayerVariables,
            _documentService.CurrentDocument.SaveVariables);
        document.Gallery = CloneGallery(_documentService.CurrentDocument.Gallery);
        return _saveCoordinator.BuildPreviewData(previewPath, document, _graphDocumentMapper.CreateGroupEntriesSnapshot(Nodes));
    }

    private static GalleryConfiguration CloneGallery(GalleryConfiguration source) => new()
    {
        Version = source.Version,
        Types = source.Types.Select(type => type with { }).ToList(),
        Items = source.Items.Select(item => item with { }).ToList()
    };
}
