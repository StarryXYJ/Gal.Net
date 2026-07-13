using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Dock.Settings;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Abstraction.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Editor.Dock;

public sealed class EditorDockFactory : Factory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEditorExtensionRegistry _extensions;
    private readonly IEditorLocalizationService _localization;
    private readonly Dictionary<IDockable, IDockPanelContribution> _panelByDockable = [];
    private InspectorHostViewModel? _inspectorHost;
    private bool _activeDockableHandlerAttached;
    private DocumentDock? _documentDock;

    public EditorDockFactory(
        IServiceProvider serviceProvider,
        IEditorExtensionRegistry extensions,
        IEditorLocalizationService localization)
    {
        _serviceProvider = serviceProvider;
        _extensions = extensions;
        _localization = localization;
        _localization.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IEditorLocalizationService.CurrentCulture) or "Item[]")
                RefreshDockTitles();
        };
    }

    public override IRootDock CreateLayout()
    {
        _documentDock = null;
        _panelByDockable.Clear();
        var panels = _extensions.DockPanelContributions.ToDictionary(panel => panel.PanelId, StringComparer.Ordinal);
        var nodeGraphDocument = CreateDocument(panels[EditorDockPanelIds.NodeGraph]);
        var previewDocument = CreateDocument(panels[EditorDockPanelIds.GamePreview]);
        var logDocument = CreateDocument(panels[EditorDockPanelIds.Log]);
        var assetsDocument = CreateDocument(panels[EditorDockPanelIds.Assets]);
        var inspectorDocument = CreateDocument(panels[EditorDockPanelIds.Inspector]);
        _inspectorHost = (InspectorHostViewModel)inspectorDocument.Context!;

        _documentDock = new DocumentDock
        {
            Id = "Documents",
            Title = "Documents",
            IsCollapsable = false,
            ActiveDockable = nodeGraphDocument,
            VisibleDockables = CreateList<IDockable>(CreateDefaultPanels(DockPanelPlacement.MainDocument, nodeGraphDocument, previewDocument).ToArray()),
            EnableGlobalDocking = true
        };

        var inspectorDock = new DocumentDock
        {
            Id = "InspectorDocuments",
            Title = "Inspector",
            ActiveDockable = inspectorDocument,
            VisibleDockables = CreateList<IDockable>([inspectorDocument]),
            EnableGlobalDocking = true
        };

        var centerDock = new ProportionalDock
        {
            Id = "Center",
            Title = "Center",
            Orientation = Orientation.Vertical,
            ActiveDockable = _documentDock,
            VisibleDockables = CreateList<IDockable>(
            [
                _documentDock,
                new ProportionalDockSplitter(),
                new DocumentDock
                {
                    Id = "LogDocuments",
                    Title = "Log",
                    ActiveDockable = logDocument,
                    VisibleDockables = CreateList<IDockable>(CreateDefaultPanels(DockPanelPlacement.BottomDocument, logDocument, assetsDocument).ToArray()),
                    EnableGlobalDocking = true
                }
            ])
        };

        var mainDock = new ProportionalDock
        {
            Id = "Main",
            Title = "Main",
            Orientation = Orientation.Horizontal,
            ActiveDockable = centerDock,
            VisibleDockables = CreateList<IDockable>(
            [
                centerDock,
                new ProportionalDockSplitter(),
                inspectorDock
            ])
        };

        var rootDock = CreateRootDock();
        rootDock.Id = "Root";
        rootDock.IsCollapsable = false;
        rootDock.ActiveDockable = mainDock;
        rootDock.DefaultDockable = mainDock;
        rootDock.VisibleDockables = CreateList<IDockable>([mainDock]);
        rootDock.EnableGlobalDocking = true;
        rootDock.Windows = CreateList<IDockWindow>();
        rootDock.LeftPinnedDockables = CreateList<IDockable>();
        rootDock.RightPinnedDockables = CreateList<IDockable>();
        rootDock.TopPinnedDockables = CreateList<IDockable>();
        rootDock.BottomPinnedDockables = CreateList<IDockable>();
        rootDock.FloatingWindowHostMode = DockFloatingWindowHostMode.Native;

        Log.Information("[DockFactory] CreateLayout done");
        ActivateInspectorFor(nodeGraphDocument);
        return rootDock;
    }

    public void OpenGroupEditor(GraphNode groupNode)
    {
        if (_documentDock?.VisibleDockables is null)
            return;

        var documentId = GetGroupEditorDocumentId(groupNode.Id);
        var existing = _documentDock.VisibleDockables.FirstOrDefault(d => d.Id == documentId);
        if (existing is not null)
        {
            _documentDock.ActiveDockable = existing;
            return;
        }

        var document = CreateDocument(_extensions.FindDockPanel(EditorDockPanelIds.GroupEditor)!, groupNode, documentId, [groupNode.Name]);

        _documentDock.VisibleDockables.Add(document);
        _documentDock.ActiveDockable = document;
    }

    public void CloseGroupEditor(string groupId)
    {
        if (_documentDock?.VisibleDockables is null)
            return;

        var documentId = GetGroupEditorDocumentId(groupId);
        var document = _documentDock.VisibleDockables.FirstOrDefault(d => d.Id == documentId);
        if (document is null)
            return;

        _documentDock.VisibleDockables.Remove(document);
        _documentDock.ActiveDockable = _documentDock.VisibleDockables.FirstOrDefault();
    }

    public override void InitLayout(IDockable layout)
    {
        Log.Information("[DockFactory] InitLayout called");

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () =>
            {
                var hostMode = DockSettings.ResolveFloatingWindowHostMode(layout as IRootDock);
                return hostMode == DockFloatingWindowHostMode.Managed
                    ? new ManagedHostWindow()
                    : new UrsaDockHostWindow();
            }
        };

        if (!_activeDockableHandlerAttached)
        {
            ActiveDockableChanged += (_, args) => ActivateInspectorFor(args.Dockable);
            _activeDockableHandlerAttached = true;
        }

        // Subscribe to factory events for debugging
        DockableAdded += (_, args) =>
            Log.Information("[DockFactory] DockableAdded: {Id}", args.Dockable?.Id);
        DockableRemoved += (_, args) =>
            Log.Information("[DockFactory] DockableRemoved: {Id}", args.Dockable?.Id);
        DockableMoved += (_, args) =>
            Log.Information("[DockFactory] DockableMoved: {Id}", args.Dockable?.Id);

        base.InitLayout(layout);
    }

    private Document CreateDocument(IDockPanelContribution panel, object? parameter = null, string? id = null, object[]? titleArguments = null)
    {
        var document = new Document { Id = id ?? panel.PanelId, Title = LocalizeTitle(panel, titleArguments), Context = panel.CreateViewModel(_serviceProvider, parameter), CanFloat = panel.CanFloat, CanClose = panel.CanClose, CanPin = false };
        _panelByDockable[document] = panel;
        return document;
    }

    private IEnumerable<IDockable> CreateDefaultPanels(DockPanelPlacement placement, params Document[] builtIns)
    {
        var documents = builtIns.Cast<IDockable>().ToList();
        foreach (var panel in _extensions.DockPanelContributions.Where(panel => panel.IsDefaultPanel && panel.Placement == placement && builtIns.All(document => document.Id != panel.PanelId)))
            documents.Add(CreateDocument(panel));
        return documents;
    }

    private void ActivateInspectorFor(IDockable? dockable)
    {
        if (dockable is null || !_panelByDockable.TryGetValue(dockable, out var panel) || panel.PanelId == EditorDockPanelIds.Inspector)
            return;
        if (panel.Inspector is null || dockable.Context is null) _inspectorHost?.ClearInspector();
        else _inspectorHost?.ShowInspectorFor(panel.PanelId, dockable.Context);
    }

    private string LocalizeTitle(IDockPanelContribution panel, object[]? arguments = null) =>
        arguments is { Length: > 0 } ? _localization.Format(panel.TitleKey, arguments) : _localization[panel.TitleKey];

    private void RefreshDockTitles()
    {
        foreach (var (dockable, panel) in _panelByDockable)
            dockable.Title = panel.PanelId == EditorDockPanelIds.GroupEditor && dockable.Context is GroupEditorPanelViewModel group
                ? LocalizeTitle(panel, [group.GroupNode.Name])
                : LocalizeTitle(panel);
    }

    private static string GetGroupEditorDocumentId(string groupId) => $"GroupEditor:{groupId}";
}
