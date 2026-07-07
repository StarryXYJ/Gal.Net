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
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Editor.Dock;

public sealed class EditorDockFactory : Factory
{
    private readonly IGamePreviewPanelFactory _previewPanelFactory;
    private readonly IServiceProvider _serviceProvider;
    private DocumentDock? _documentDock;

    public EditorDockFactory(
        IGamePreviewPanelFactory previewPanelFactory,
        IServiceProvider serviceProvider)
    {
        _previewPanelFactory = previewPanelFactory;
        _serviceProvider = serviceProvider;
    }

    public override IRootDock CreateLayout()
    {
        _documentDock = null;

        var inspectorTool = CreateTool(
            "Inspector",
            "Inspector",
            _serviceProvider.GetRequiredService<NodeInspectorPanelViewModel>());
        var logTool = CreateTool(
            "Log",
            "Log",
            _serviceProvider.GetRequiredService<LogPanelViewModel>());

        var rightDock = new ToolDock
        {
            Id = "RightTools",
            Title = "Right",
            ActiveDockable = inspectorTool,
            VisibleDockables = CreateList<IDockable>([inspectorTool]),
            Alignment = Alignment.Right,
            IsExpanded = true
        };

        var bottomDock = new ToolDock
        {
            Id = "BottomTools",
            Title = "Bottom",
            ActiveDockable = logTool,
            VisibleDockables = CreateList<IDockable>([logTool]),
            Alignment = Alignment.Bottom,
            IsExpanded = true
        };

        var nodeGraphDocument = new Document
        {
            Id = "NodeGraph",
            Title = "Node Graph",
            Context = _serviceProvider.GetRequiredService<NodeGraphPanelViewModel>(),
            CanFloat = true,
            CanClose = false,
            CanPin = false
        };
        var previewVm = _previewPanelFactory.Create();
        var previewDocument = new Document
        {
            Id = "GamePreview",
            Title = "Game Preview",
            Context = previewVm,
            CanFloat = true,
            CanClose = true,
            CanPin = false
        };

        _documentDock = new DocumentDock
        {
            Id = "Documents",
            Title = "Documents",
            IsCollapsable = false,
            ActiveDockable = nodeGraphDocument,
            VisibleDockables = CreateList<IDockable>([nodeGraphDocument, previewDocument]),
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
                bottomDock
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
                rightDock
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
        return rootDock;
    }

    public void OpenGroupEditor(GraphNodeViewModel groupNode)
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

        var document = new Document
        {
            Id = documentId,
            Title = $"Group: {groupNode.Name}",
            Context = new GroupEditorPanelViewModel(groupNode),
            CanFloat = true,
            CanClose = true,
            CanPin = false
        };

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

        // Subscribe to factory events for debugging
        DockableAdded += (_, args) =>
            Log.Information("[DockFactory] DockableAdded: {Id}", args.Dockable?.Id);
        DockableRemoved += (_, args) =>
            Log.Information("[DockFactory] DockableRemoved: {Id}", args.Dockable?.Id);
        DockableMoved += (_, args) =>
            Log.Information("[DockFactory] DockableMoved: {Id}", args.Dockable?.Id);

        base.InitLayout(layout);
    }

    private static Tool CreateTool(string id, string title, object context) =>
        new()
        {
            Id = id,
            Title = title,
            Context = context,
            CanFloat = true,
            CanClose = true,
            CanPin = true
        };

    private static string GetGroupEditorDocumentId(string groupId) => $"GroupEditor:{groupId}";
}
