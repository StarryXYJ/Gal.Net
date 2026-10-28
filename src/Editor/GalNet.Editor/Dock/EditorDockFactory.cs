using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Dock.Settings;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Editor.Dock;

public sealed class EditorDockFactory : Factory, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEditorExtensionRegistry _extensions;
    private readonly IEditorLocalizationService _localization;
    private readonly DockInspectorCoordinator _inspector;
    private readonly Dictionary<IDockable, IDockPanelContribution> _panelByDockable = [];
    private EditorWorkspaceViewModel? _workspace;
    private bool _activeDockableHandlerAttached;
    private bool _dockableEventHandlersAttached;
    private DocumentDock? _documentDock;
    private IRootDock? _layoutRoot;
    private DocumentDock? _presetInspector;
    private (IDock Owner, IDockable Dock, int Index, IDockable[] RootItems, IDockable? RootActive)? _maximized;
    public bool IsMaximized => _maximized is not null;
    private readonly PropertyChangedEventHandler _localizationChanged;

    /// <summary>Raised after a panel is created, closed, moved, or activated.</summary>
    public event Action? LayoutChanged;

    public EditorDockFactory(
        IServiceProvider serviceProvider,
        IEditorExtensionRegistry extensions,
        IEditorLocalizationService localization)
    {
        _serviceProvider = serviceProvider;
        _extensions = extensions;
        _localization = localization;
        _inspector = new DockInspectorCoordinator(_panelByDockable, () =>
            serviceProvider.GetService<IProjectService>()?.Current?.Services.GetService<EditorWorkspaceViewModel>());
        _localizationChanged = (_, e) =>
        {
            if (e.PropertyName is nameof(IEditorLocalizationService.CurrentCulture) or "Item[]")
                RefreshDockTitles();
        };
        _localization.PropertyChanged += _localizationChanged;
    }

    // ── Layout creation ──

    public override IRootDock CreateLayout()
    {
        _maximized = null;
        _documentDock = null;
        DisposeGroupPages();
        _panelByDockable.Clear();
        _inspector.Clear();
        AttachWorkspace();

        var panels = _extensions.DockPanelContributions.ToDictionary(panel => panel.PanelId, StringComparer.Ordinal);
        var nodeGraphDocument = CreateDocument(panels[EditorDockPanelIds.NodeGraph]);
        var logDocument = CreateDocument(panels[EditorDockPanelIds.Log]);
        var assetsDocument = CreateDocument(panels[EditorDockPanelIds.Assets]);
        var inspectorDocument = CreateDocument(panels[EditorDockPanelIds.Inspector]);
        _inspector.Register((InspectorHostViewModel)inspectorDocument.Context!);

        _documentDock = new DocumentDock
        {
            Id = "Documents",
            Title = "Documents",
            IsCollapsable = false,
            ActiveDockable = nodeGraphDocument,
            VisibleDockables = CreateList<IDockable>(CreateDefaultPanels(DockPanelPlacement.MainDocument, nodeGraphDocument).ToArray()),
            EnableGlobalDocking = true
        };

        var inspectorDock = new DocumentDock
        {
            Id = "InspectorDocuments",
            Title = "Inspector",
            Proportion = 0.28,
            ActiveDockable = inspectorDocument,
            VisibleDockables = CreateList<IDockable>([inspectorDocument]),
            EnableGlobalDocking = true
        };

        var centerDock = new ProportionalDock
        {
            Id = "Center",
            Title = "Center",
            Proportion = 0.72,
            Orientation = Orientation.Vertical,
            ActiveDockable = _documentDock,
            VisibleDockables = CreateList<IDockable>(
            [
                _documentDock,
                new ProportionalDockSplitter(),
                new DocumentDock
                {
                    Id = "LogDocuments", Title = "Log",
                    Proportion = 0.22,
                    ActiveDockable = assetsDocument,
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
            VisibleDockables = CreateList<IDockable>([centerDock, new ProportionalDockSplitter(), inspectorDock])
        };

        var rootDock = CreateRootDock();
        _documentDock.Proportion = 0.78;
        _presetInspector = inspectorDock;
        rootDock.Id = "Root"; rootDock.IsCollapsable = false;
        rootDock.ActiveDockable = mainDock; rootDock.DefaultDockable = mainDock;
        rootDock.VisibleDockables = CreateList<IDockable>([mainDock]);
        rootDock.EnableGlobalDocking = true;
        rootDock.Windows = CreateList<IDockWindow>();
        rootDock.LeftPinnedDockables = CreateList<IDockable>();
        rootDock.RightPinnedDockables = CreateList<IDockable>();
        rootDock.TopPinnedDockables = CreateList<IDockable>();
        rootDock.BottomPinnedDockables = CreateList<IDockable>();
        (_serviceProvider.GetService<IProjectService>()?.Current?.Services ?? _serviceProvider)
            .GetService<UndoRedoRouter>()?.SetActive(nodeGraphDocument.Context);
        rootDock.FloatingWindowHostMode = DockFloatingWindowHostMode.Native;

        Log.Information("[DockFactory] CreateLayout done");
        _inspector.UpdateFor(nodeGraphDocument);
        LayoutChanged?.Invoke();
        return rootDock;
    }

    // ── Restore ──

    public bool PrepareRestoredLayout(IRootDock layout)
    {
        _maximized = null;
        DisposeGroupPages();
        _panelByDockable.Clear();
        _inspector.Clear();
        AttachWorkspace();
        _documentDock = FindDockById(layout, "Documents") as DocumentDock;

        var documents = EnumerateDockables(layout).OfType<Document>().ToList();
        var hasInvalidDocument = false;
        foreach (var document in documents)
        {
            var panel = FindPanelForDocument(document.Id);
            if (panel is null)
            {
                if (document.Owner is IDock owner && owner.VisibleDockables is { } ownerItems)
                {
                    ownerItems.Remove(document);
                    if (ReferenceEquals(owner.ActiveDockable, document))
                        owner.ActiveDockable = ownerItems.FirstOrDefault(item => item is not IProportionalDockSplitter);
                }
                else hasInvalidDocument = true;
                continue;
            }

            var services = _serviceProvider.GetService<IProjectService>()?.Current?.Services ?? _serviceProvider;
            if (panel.PanelId == EditorDockPanelIds.FileEditor) { RemoveRestoredDocument(document); continue; }
            if (panel.PanelId == EditorDockPanelIds.GroupEditor)
            {
                var groupId = GetGroupEditorId(document.Id);
                var group = _workspace?.Nodes.FirstOrDefault(node => node.Id == groupId && node.NodeKind == GraphNodeKind.LinearGroup);
                if (group is null)
                {
                    RemoveRestoredDocument(document);
                    continue;
                }

                document.Context = panel.CreateViewModel(services, group.Id);
                document.Title = LocalizeTitle(panel, [group.Name]);
            }
            else
            {
                document.Context = panel.CreateViewModel(services);
                document.Title = LocalizeTitle(panel);
            }
            document.CanClose = panel.CanClose;
            document.CanFloat = panel.CanFloat;
            _panelByDockable[document] = panel;
            if (document.Context is InspectorHostViewModel inspector)
                _inspector.Register(inspector);
        }

        var activeDockable = FindActiveInspectableDockable(layout);
        if (activeDockable is not null)
            _inspector.UpdateFor(activeDockable);

        return !hasInvalidDocument
               && _documentDock is not null
               && _panelByDockable.Count > 0
               && _panelByDockable.Keys.All(document => document.Context is not null);
    }

    public IReadOnlyList<IDockPanelContribution> ViewMenuPanels =>
        _extensions.DockPanelContributions.Where(panel => panel.ShowInViewMenu).ToList();

    // ── Panel operations ──

    public bool HasGlobalPanel(string panelId) => _panelByDockable.Any(pair =>
        pair.Value.PanelId == panelId && pair.Value.IsGlobal && IsAttached(pair.Key));

    public void ToggleGlobalPanel(string panelId)
    {
        var panel = _extensions.FindDockPanel(panelId);
        if (panel is null || !panel.IsGlobal) return;

        var existing = _panelByDockable.FirstOrDefault(pair => pair.Value.PanelId == panelId && IsAttached(pair.Key)).Key;
        if (existing is not null)
            CloseDocument(existing);
        else
            OpenPanel(panelId);
    }

    public void OpenPanel(string panelId)
    {
        if (panelId == EditorDockPanelIds.Inspector && _layoutRoot is not null && _presetInspector is { Owner: null } inspectorDock
            && FindDockById(_layoutRoot, "Main") is ProportionalDock main)
        {
            main.VisibleDockables ??= CreateList<IDockable>();
            main.VisibleDockables.Add(new ProportionalDockSplitter()); main.VisibleDockables.Add(inspectorDock); inspectorDock.Owner = main;
        }
        var panel = _extensions.FindDockPanel(panelId);
        if (panel is null) return;

        if (panel.IsGlobal)
        {
            var existing = _panelByDockable.FirstOrDefault(pair => pair.Value.PanelId == panelId && IsAttached(pair.Key)).Key;
            if (existing is not null) { Activate(existing); return; }
        }

        var document = CreateDocument(panel, id: panel.IsGlobal ? panel.PanelId : $"{panel.PanelId}:{Guid.NewGuid():N}");
        AddDocument(document, panel);
        if (document.Context is InspectorHostViewModel inspector)
        {
            _inspector.Register(inspector);
            _inspector.InitializeFor(inspector, () => FindActiveInspectableDockable(FactoryExtensions.GetActiveRoot(this)));
        }
        Activate(document);
        LayoutChanged?.Invoke();
    }

    public void ShowInspector()
    {
        var existing = _panelByDockable.FirstOrDefault(pair => pair.Value.PanelId == EditorDockPanelIds.Inspector
            && pair.Key.Context is InspectorHostViewModel { IsLocked: false } && IsAttached(pair.Key)).Key;
        if (existing is null) { OpenPanel(EditorDockPanelIds.Inspector); return; }
        if (existing.Owner is DocumentDock { Owner: null } inspectorDock && _layoutRoot is not null
            && FindDockById(_layoutRoot, "Main") is ProportionalDock main)
        {
            main.VisibleDockables ??= CreateList<IDockable>(); main.VisibleDockables.Add(new ProportionalDockSplitter());
            main.VisibleDockables.Add(inspectorDock); inspectorDock.Owner = main;
        }
        Activate(existing);
    }

    public void OpenGroupEditor(GraphNode groupNode)
    {
        if (_documentDock?.VisibleDockables is null) return;

        var documentId = GetGroupEditorDocumentId(groupNode.Id);
        var existing = _documentDock.VisibleDockables.FirstOrDefault(d => d.Id == documentId);
        if (existing is not null) { _documentDock.ActiveDockable = existing; (existing.Context as GroupEditorPanelViewModel)?.PublishSelection(); return; }

        var document = CreateDocument(_extensions.FindDockPanel(EditorDockPanelIds.GroupEditor)!, groupNode.Id, documentId, [groupNode.Name]);
        _documentDock.VisibleDockables.Add(document);
        _documentDock.ActiveDockable = document;
        (document.Context as GroupEditorPanelViewModel)?.PublishSelection();
        LayoutChanged?.Invoke();
    }

    public void OpenTimeline(string groupId, string entryId)
    {
        var group = _workspace?.Nodes.FirstOrDefault(node => node.Id == groupId);
        var entry = group?.Entries.FirstOrDefault(item => item.StableId == entryId);
        if (group is null || entry is null || entry.Type != GalNet.Contracts.Builtins.BuiltinEntryIds.PlayTimeline) return;
        OpenGroupEditor(group);
        var page = _documentDock?.VisibleDockables?.FirstOrDefault(item => item.Id == GetGroupEditorDocumentId(groupId))?.Context as GroupEditorPanelViewModel;
        if (page is null || page.IsDisposed) return;
        page.SelectedEntry = entry; page.PublishSelection();
        OpenPanel(EditorDockPanelIds.Timeline);
    }

    public void NavigateLocalization(string owner, string? entryId)
    {
        if (owner == "characters") { OpenPanel(EditorDockPanelIds.Characters); return; }
        var node = _workspace?.Nodes.FirstOrDefault(node => node.Id == owner);
        if (node is null) return;
        _workspace!.SelectNode(node);
        if (node.NodeKind != GalNet.Editor.Models.Graph.GraphNodeKind.LinearGroup) { OpenPanel(EditorDockPanelIds.NodeGraph); ShowInspector(); return; }
        OpenGroupEditor(node);
        var page = _documentDock?.VisibleDockables?.FirstOrDefault(item => item.Id == GetGroupEditorDocumentId(owner))?.Context as GroupEditorPanelViewModel;
        if (page is null || page.IsDisposed) return;
        page.SearchText = "";
        page.SelectedEntry = node.Entries.FirstOrDefault(entry => entry.StableId == entryId); page.PublishSelection();
    }

    public void CloseGroupEditor(string groupId)
    {
        if (_documentDock?.VisibleDockables is null) return;
        var document = _documentDock.VisibleDockables.FirstOrDefault(d => d.Id == GetGroupEditorDocumentId(groupId));
        if (document is not null) CloseDocument(document);
    }

    public void OpenFileEditor(ProjectTextBuffer buffer)
    {
        if (_documentDock?.VisibleDockables is null) return;
        var id = "FileEditor:" + buffer.Id;
        var existing = _panelByDockable.Keys.FirstOrDefault(item => item.Id == id);
        if (existing is not null) { if (existing.Owner is null) _documentDock.ActiveDockable = existing; else Activate(existing); return; }
        var document = CreateDocument(_extensions.FindDockPanel(EditorDockPanelIds.FileEditor)!, buffer.Id, id);
        var viewModel = (FileDocumentPanelViewModel)document.Context!;
        document.Title = viewModel.Title;
        viewModel.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(FileDocumentPanelViewModel.Title)) document.Title = viewModel.Title; };
        AddDocument(document, _extensions.FindDockPanel(EditorDockPanelIds.FileEditor)!); Activate(document);
        LayoutChanged?.Invoke();
    }

    // ── Init ──

    public override void InitLayout(IDockable layout)
    {
        _layoutRoot = layout as IRootDock;
        _presetInspector = FindDockById(layout, "InspectorDocuments") as DocumentDock ?? _presetInspector;
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
            ActiveDockableChanged += (_, args) =>
            {
                _inspector.UpdateFor(args.Dockable);
                if (args.Dockable?.Context is GroupEditorPanelViewModel group) group.PublishSelection();
                var services = _serviceProvider.GetService<IProjectService>()?.Current?.Services;
                services?.GetService<UndoRedoRouter>()?.SetActive(args.Dockable?.Context);
                LayoutChanged?.Invoke();
            };
            FocusedDockableChanged += (_, args) =>
            {
                _inspector.UpdateFor(args.Dockable);
                if (args.Dockable?.Context is GroupEditorPanelViewModel group) group.PublishSelection();
                var services = _serviceProvider.GetService<IProjectService>()?.Current?.Services;
                services?.GetService<UndoRedoRouter>()?.SetActive(args.Dockable?.Context);
            };
            _activeDockableHandlerAttached = true;
        }

        if (!_dockableEventHandlersAttached)
        {
            DockableAdded += (_, args) => { Log.Information("[DockFactory] DockableAdded: {Id}", args.Dockable?.Id); LayoutChanged?.Invoke(); };
            DockableRemoved += (_, args) => { Log.Information("[DockFactory] DockableRemoved: {Id}", args.Dockable?.Id); LayoutChanged?.Invoke(); };
            DockableClosed += (_, args) =>
            {
                if (args.Dockable is not { } closed) return;
                if (closed.Context is GroupEditorPanelViewModel group) group.Dispose();
                if (closed.Context is FileDocumentPanelViewModel file) file.Dispose();
                if (closed.Context is VariablesPanelViewModel variables) variables.Dispose();
                if (closed.Context is CharactersPanelViewModel characters) characters.Dispose();
                if (closed.Context is TimelinePanelViewModel timeline) timeline.Dispose();
                if (closed.Context is LogPanelViewModel log) log.Dispose();
                if (closed.Context is InspectorHostViewModel) _inspector.RemoveInspector(closed);
                _panelByDockable.Remove(closed);
            };
            DockableMoved += (_, args) => { Log.Information("[DockFactory] DockableMoved: {Id}", args.Dockable?.Id); LayoutChanged?.Invoke(); };
            _dockableEventHandlersAttached = true;
        }

        base.InitLayout(layout);
    }

    public void ApplyWorkspacePreset(string preset)
    {
        if (preset is not ("Writing" or "Animation" or "Debug") || _layoutRoot is null) return;
        if (_maximized is not null) ToggleMaximizeActive();
        var main = FindDockById(_layoutRoot, "Main") as ProportionalDock;
        var center = FindDockById(_layoutRoot, "Center") as ProportionalDock;
        var bottom = FindDockById(_layoutRoot, "LogDocuments") as DocumentDock;
        var documents = FindDockById(_layoutRoot, "Documents") as DocumentDock;
        if (main is null || center is null || bottom is null || documents is null)
        {
            RebuildWorkspaceContainers();
            main = FindDockById(_layoutRoot, "Main") as ProportionalDock;
            center = FindDockById(_layoutRoot, "Center") as ProportionalDock;
            bottom = FindDockById(_layoutRoot, "LogDocuments") as DocumentDock;
            documents = _documentDock;
        }
        if (main is null || center is null || bottom is null || documents is null) return;
        var height = preset == "Writing" ? 0.22 : preset == "Animation" ? 0.45 : 0.40;
        bottom.Proportion = height; documents.Proportion = 1 - height;
        main.VisibleDockables = CreateList<IDockable>([center]); center.Owner = main;
        center.Proportion = preset == "Animation" ? 1 : 0.72;
        if (_presetInspector is { } inspector)
        {
            inspector.Owner = null;
            if (preset != "Animation")
            {
                inspector.Proportion = 0.28; inspector.Owner = main;
                main.VisibleDockables.Add(new ProportionalDockSplitter()); main.VisibleDockables.Add(inspector);
            }
        }
        if (preset != "Animation" && _presetInspector?.VisibleDockables?.Any(item => item.Context is InspectorHostViewModel) != true)
            OpenPanel(EditorDockPanelIds.Inspector);
        var id = preset == "Writing" ? EditorDockPanelIds.Assets : preset == "Animation" ? EditorDockPanelIds.Timeline : EditorDockPanelIds.ExternalDebug;
        OpenPanel(id);
        if (documents.ActiveDockable is { } authorTarget) _inspector.UpdateFor(authorTarget);
        LayoutChanged?.Invoke();
    }

    private void RebuildWorkspaceContainers()
    {
        if (_layoutRoot is null) return;
        var activeDocument = _documentDock?.ActiveDockable;
        var documents = new DocumentDock { Id = "Documents", IsCollapsable = false, EnableGlobalDocking = true, VisibleDockables = CreateList<IDockable>() };
        var bottom = new DocumentDock { Id = "LogDocuments", EnableGlobalDocking = true, VisibleDockables = CreateList<IDockable>() };
        var inspector = new DocumentDock { Id = "InspectorDocuments", EnableGlobalDocking = true, VisibleDockables = CreateList<IDockable>() };
        // Dock may collapse or replace empty containers after a user closes/moves a panel.
        // Reparent existing documents, preserving their contexts, selections, and draft leases.
        foreach (var pair in _panelByDockable.ToArray())
        {
            if (pair.Key is not Document document) continue;
            if (document.Owner is IDock oldOwner) oldOwner.VisibleDockables?.Remove(document);
            var owner = pair.Value.Placement switch
            {
                DockPanelPlacement.BottomDocument => bottom,
                DockPanelPlacement.InspectorDocument => inspector,
                _ => documents
            };
            owner.VisibleDockables!.Add(document); document.Owner = owner;
        }
        documents.ActiveDockable = activeDocument is not null && documents.VisibleDockables!.Contains(activeDocument) ? activeDocument : documents.VisibleDockables!.FirstOrDefault();
        bottom.ActiveDockable = bottom.VisibleDockables!.FirstOrDefault(); inspector.ActiveDockable = inspector.VisibleDockables!.FirstOrDefault();
        var center = new ProportionalDock
        {
            Id = "Center",
            Orientation = Orientation.Vertical,
            ActiveDockable = documents,
            VisibleDockables = CreateList<IDockable>([documents, new ProportionalDockSplitter(), bottom])
        };
        var main = new ProportionalDock
        {
            Id = "Main",
            Orientation = Orientation.Horizontal,
            ActiveDockable = center,
            VisibleDockables = CreateList<IDockable>([center, new ProportionalDockSplitter(), inspector])
        };
        _layoutRoot.VisibleDockables = CreateList<IDockable>([main]); _layoutRoot.ActiveDockable = main; _layoutRoot.DefaultDockable = main;
        _documentDock = documents; _presetInspector = inspector;
        InitLayout(_layoutRoot);
    }

    public void ToggleMaximizeActive()
    {
        if (_layoutRoot is null) return;
        if (_maximized is { } saved)
        {
            saved.Owner.VisibleDockables!.Insert(Math.Min(saved.Index, saved.Owner.VisibleDockables.Count), saved.Dock);
            saved.Dock.Owner = saved.Owner;
            _layoutRoot.VisibleDockables = CreateList<IDockable>(saved.RootItems); _layoutRoot.ActiveDockable = saved.RootActive;
            _maximized = null;
        }
        else
        {
            var target = FindActiveInspectableDockable(_layoutRoot);
            if (target?.Owner is not IDock dock || dock.Owner is not IDock owner || owner.VisibleDockables is null) return;
            var index = owner.VisibleDockables.IndexOf(dock);
            if (index < 0) return;
            _maximized = (owner, dock, index, _layoutRoot.VisibleDockables?.ToArray() ?? [], _layoutRoot.ActiveDockable);
            owner.VisibleDockables.Remove(dock); dock.Owner = _layoutRoot;
            _layoutRoot.VisibleDockables = CreateList<IDockable>([dock]); _layoutRoot.ActiveDockable = dock;
        }
        LayoutChanged?.Invoke();
    }

    // ── Document helpers ──

    private Document CreateDocument(IDockPanelContribution panel, object? parameter = null, string? id = null, object[]? titleArguments = null)
    {
        var services = _serviceProvider.GetService<IProjectService>()?.Current?.Services ?? _serviceProvider;
        var document = new Document { Id = id ?? panel.PanelId, Title = LocalizeTitle(panel, titleArguments), Context = panel.CreateViewModel(services, parameter), CanFloat = panel.CanFloat, CanClose = panel.CanClose, CanPin = false };
        _panelByDockable[document] = panel;
        return document;
    }

    private List<IDockable> CreateDefaultPanels(DockPanelPlacement placement, params Document[] builtIns)
    {
        var documents = builtIns.Cast<IDockable>().ToList();
        foreach (var panel in _extensions.DockPanelContributions.Where(panel => panel.IsDefaultPanel && panel.Placement == placement && builtIns.All(document => document.Id != panel.PanelId)))
            documents.Add(CreateDocument(panel));
        return documents;
    }

    private string LocalizeTitle(IDockPanelContribution panel, object[]? arguments = null) =>
        arguments is { Length: > 0 } ? _localization.Format(panel.TitleKey, arguments) : _localization[panel.TitleKey];

    private void RefreshDockTitles()
    {
        foreach (var (dockable, panel) in _panelByDockable)
            dockable.Title = panel.PanelId == EditorDockPanelIds.GroupEditor && dockable.Context is GroupEditorPanelViewModel group
                ? LocalizeTitle(panel, [group.GroupNode.Name])
                : dockable.Context is FileDocumentPanelViewModel file ? file.Title : LocalizeTitle(panel);
    }

    private static string GetGroupEditorDocumentId(string groupId) => $"GroupEditor:{groupId}";

    private static string? GetGroupEditorId(string? documentId) =>
        documentId?.StartsWith(EditorDockPanelIds.GroupEditor + ":", StringComparison.Ordinal) == true
            ? documentId[(EditorDockPanelIds.GroupEditor.Length + 1)..]
            : null;

    private static void RemoveRestoredDocument(Document document)
    {
        if (document.Owner is not IDock owner || owner.VisibleDockables is null)
            return;

        owner.VisibleDockables.Remove(document);
        if (ReferenceEquals(owner.ActiveDockable, document))
            owner.ActiveDockable = owner.VisibleDockables.FirstOrDefault(item => item is not IProportionalDockSplitter);
    }

    private void AddDocument(Document document, IDockPanelContribution panel)
    {
        var target = FindDocumentDock(panel);
        target.VisibleDockables ??= CreateList<IDockable>();
        target.VisibleDockables.Add(document);
        target.ActiveDockable = document;
    }

    private DocumentDock FindDocumentDock(IDockPanelContribution panel)
    {
        var existing = _panelByDockable.FirstOrDefault(pair => pair.Value.PanelId == panel.PanelId && pair.Key.Owner is DocumentDock).Key;
        if (existing?.Owner is DocumentDock sameTypeDock) return sameTypeDock;

        var id = panel.Placement switch
        {
            DockPanelPlacement.MainDocument => "Documents",
            DockPanelPlacement.BottomDocument => "LogDocuments",
            DockPanelPlacement.InspectorDocument => "InspectorDocuments",
            _ => "Documents"
        };
        return FindDockById(FactoryExtensions.GetActiveRoot(this), id) as DocumentDock ?? _documentDock!;
    }

    private void CloseDocument(IDockable document)
    {
        if (document.Owner is not IDock owner || owner.VisibleDockables is null) return;

        owner.VisibleDockables.Remove(document);
        owner.ActiveDockable = owner.VisibleDockables.FirstOrDefault();
        _inspector.RemoveInspectorAndRecover(document, () => FindActiveInspectableDockable(FactoryExtensions.GetActiveRoot(this)));
        _panelByDockable.Remove(document);
        // Localization drafts belong to the project scope and survive dock closure.
        if (document.Context is not (GameLocalizationPanelViewModel or ResourceOverridesPanelViewModel or AssetPanelViewModel or ProblemsPanelViewModel or EditorWorkspaceViewModel))
            (document.Context as IDisposable)?.Dispose();
        LayoutChanged?.Invoke();
    }

    public void Dispose()
    {
        _localization.PropertyChanged -= _localizationChanged;
        if (_workspace is not null) _workspace.PropertyChanged -= OnWorkspacePropertyChanged;
        foreach (var dockable in _panelByDockable.Keys)
            if (!ReferenceEquals(dockable.Context, _workspace)) (dockable.Context as IDisposable)?.Dispose();
        _panelByDockable.Clear();
        _inspector.Clear();
    }

    private void DisposeGroupPages()
    {
        foreach (var page in _panelByDockable.Keys.Select(dockable => dockable.Context).OfType<GroupEditorPanelViewModel>())
            page.Dispose();
        foreach (var page in _panelByDockable.Keys.Select(dockable => dockable.Context).OfType<FileDocumentPanelViewModel>()) page.Dispose();
        foreach (var page in _panelByDockable.Keys.Select(dockable => dockable.Context).OfType<VariablesPanelViewModel>()) page.Dispose();
        foreach (var page in _panelByDockable.Keys.Select(dockable => dockable.Context).OfType<CharactersPanelViewModel>()) page.Dispose();
        foreach (var page in _panelByDockable.Keys.Select(dockable => dockable.Context).OfType<TimelinePanelViewModel>()) page.Dispose();
    }

    // ── Utility ──

    private static IDockable? FindDockById(IDockable? root, string id)
    {
        if (root?.Id == id) return root;
        return root is IDock dock && dock.VisibleDockables is not null
            ? dock.VisibleDockables.Select(child => FindDockById(child, id)).FirstOrDefault(found => found is not null)
            : null;
    }

    private IDockPanelContribution? FindPanelForDocument(string? documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId)) return null;
        return _extensions.DockPanelContributions.FirstOrDefault(panel =>
            documentId.Equals(panel.PanelId, StringComparison.Ordinal) ||
            documentId.StartsWith(panel.PanelId + ":", StringComparison.Ordinal));
    }

    private static IEnumerable<IDockable> EnumerateDockables(IDockable dockable)
    {
        yield return dockable;
        if (dockable is not IDock dock || dock.VisibleDockables is null) yield break;
        foreach (var child in dock.VisibleDockables)
            foreach (var descendant in EnumerateDockables(child))
                yield return descendant;
    }

    private IDockable? FindActiveInspectableDockable(IDockable? root)
    {
        if (root is IDock dock && dock.ActiveDockable is { } active)
        {
            var nested = FindActiveInspectableDockable(active);
            if (nested is not null) return nested;
        }

        return root is not null && _panelByDockable.TryGetValue(root, out var panel) && panel.PanelId != EditorDockPanelIds.Inspector
            ? root : null;
    }

    private static bool IsAttached(IDockable dockable) => dockable.Owner is not null;

    private static void Activate(IDockable dockable)
    {
        if (dockable.Owner is IDock owner) owner.ActiveDockable = dockable;
    }

    // ── Workspace ──

    private void AttachWorkspace()
    {
        var workspace = _serviceProvider.GetService<IProjectService>()?.Current?.Services.GetService<EditorWorkspaceViewModel>();
        if (ReferenceEquals(workspace, _workspace)) return;

        if (_workspace is not null)
            _workspace.PropertyChanged -= OnWorkspacePropertyChanged;

        _workspace = workspace;
        if (_workspace is not null)
            _workspace.PropertyChanged += OnWorkspacePropertyChanged;
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _inspector.OnWorkspacePropertyChanged(sender, e, panelId =>
        {
            if (panelId is null) return null;
            return _panelByDockable.FirstOrDefault(pair => pair.Value.PanelId == panelId && IsAttached(pair.Key)).Key;
        });
    }
}
