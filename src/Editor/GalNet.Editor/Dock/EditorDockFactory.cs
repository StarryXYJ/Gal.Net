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
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.History;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Editor.Dock;

public sealed class EditorDockFactory : Factory
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
        _localization.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IEditorLocalizationService.CurrentCulture) or "Item[]")
                RefreshDockTitles();
        };
    }

    // ── Layout creation ──

    public override IRootDock CreateLayout()
    {
        _documentDock = null;
        _panelByDockable.Clear();
        _inspector.Clear();
        AttachWorkspace();

        var panels = _extensions.DockPanelContributions.ToDictionary(panel => panel.PanelId, StringComparer.Ordinal);
        var nodeGraphDocument = CreateDocument(panels[EditorDockPanelIds.NodeGraph]);
        var previewDocument = CreateDocument(panels[EditorDockPanelIds.GamePreview]);
        var logDocument = CreateDocument(panels[EditorDockPanelIds.Log]);
        var assetsDocument = CreateDocument(panels[EditorDockPanelIds.Assets]);
        var inspectorDocument = CreateDocument(panels[EditorDockPanelIds.Inspector]);
        _inspector.Register((InspectorHostViewModel)inspectorDocument.Context!);

        _documentDock = new DocumentDock
        {
            Id = "Documents", Title = "Documents", IsCollapsable = false,
            ActiveDockable = nodeGraphDocument,
            VisibleDockables = CreateList<IDockable>(CreateDefaultPanels(DockPanelPlacement.MainDocument, nodeGraphDocument, previewDocument).ToArray()),
            EnableGlobalDocking = true
        };

        var inspectorDock = new DocumentDock
        {
            Id = "InspectorDocuments", Title = "Inspector", Proportion = 0.32,
            ActiveDockable = inspectorDocument,
            VisibleDockables = CreateList<IDockable>([inspectorDocument]),
            EnableGlobalDocking = true
        };

        var centerDock = new ProportionalDock
        {
            Id = "Center", Title = "Center", Proportion = 0.68, Orientation = Orientation.Vertical,
            ActiveDockable = _documentDock,
            VisibleDockables = CreateList<IDockable>(
            [
                _documentDock,
                new ProportionalDockSplitter(),
                new DocumentDock
                {
                    Id = "LogDocuments", Title = "Log",
                    ActiveDockable = logDocument,
                    VisibleDockables = CreateList<IDockable>(CreateDefaultPanels(DockPanelPlacement.BottomDocument, logDocument, assetsDocument).ToArray()),
                    EnableGlobalDocking = true
                }
            ])
        };

        var mainDock = new ProportionalDock
        {
            Id = "Main", Title = "Main", Orientation = Orientation.Horizontal,
            ActiveDockable = centerDock,
            VisibleDockables = CreateList<IDockable>([centerDock, new ProportionalDockSplitter(), inspectorDock])
        };

        var rootDock = CreateRootDock();
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
            if (panel.PanelId == EditorDockPanelIds.GroupEditor)
            {
                var groupId = GetGroupEditorId(document.Id);
                var group = _workspace?.Nodes.FirstOrDefault(node => node.Id == groupId && node.NodeKind == GraphNodeKind.LinearGroup);
                if (group is null)
                {
                    RemoveRestoredDocument(document);
                    continue;
                }

                document.Context = panel.CreateViewModel(services, group);
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

    public void OpenGroupEditor(GraphNode groupNode)
    {
        if (_documentDock?.VisibleDockables is null) return;

        var documentId = GetGroupEditorDocumentId(groupNode.Id);
        var existing = _documentDock.VisibleDockables.FirstOrDefault(d => d.Id == documentId);
        if (existing is not null) { _documentDock.ActiveDockable = existing; return; }

        var document = CreateDocument(_extensions.FindDockPanel(EditorDockPanelIds.GroupEditor)!, groupNode, documentId, [groupNode.Name]);
        _documentDock.VisibleDockables.Add(document);
        _documentDock.ActiveDockable = document;
        LayoutChanged?.Invoke();
    }

    public void CloseGroupEditor(string groupId)
    {
        if (_documentDock?.VisibleDockables is null) return;
        var document = _documentDock.VisibleDockables.FirstOrDefault(d => d.Id == GetGroupEditorDocumentId(groupId));
        if (document is not null) CloseDocument(document);
    }

    // ── Init ──

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
            ActiveDockableChanged += (_, args) =>
            {
                _inspector.UpdateFor(args.Dockable);
                var services = _serviceProvider.GetService<IProjectService>()?.Current?.Services;
                services?.GetService<UndoRedoRouter>()?.SetActive(args.Dockable?.Context);
                LayoutChanged?.Invoke();
            };
            _activeDockableHandlerAttached = true;
        }

        if (!_dockableEventHandlersAttached)
        {
            DockableAdded += (_, args) => { Log.Information("[DockFactory] DockableAdded: {Id}", args.Dockable?.Id); LayoutChanged?.Invoke(); };
            DockableRemoved += (_, args) => { Log.Information("[DockFactory] DockableRemoved: {Id}", args.Dockable?.Id); LayoutChanged?.Invoke(); };
            DockableMoved += (_, args) => { Log.Information("[DockFactory] DockableMoved: {Id}", args.Dockable?.Id); LayoutChanged?.Invoke(); };
            _dockableEventHandlersAttached = true;
        }

        base.InitLayout(layout);
    }

    // ── Document helpers ──

    private Document CreateDocument(IDockPanelContribution panel, object? parameter = null, string? id = null, object[]? titleArguments = null)
    {
        var services = _serviceProvider.GetService<IProjectService>()?.Current?.Services ?? _serviceProvider;
        var document = new Document { Id = id ?? panel.PanelId, Title = LocalizeTitle(panel, titleArguments), Context = panel.CreateViewModel(services, parameter), CanFloat = panel.CanFloat, CanClose = panel.CanClose, CanPin = false };
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
        LayoutChanged?.Invoke();
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
