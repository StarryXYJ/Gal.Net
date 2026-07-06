using System;
using System.Collections.Generic;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Dock.Settings;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Editor.Dock;

public sealed class EditorDockFactory : Factory
{
    private readonly IServiceProvider _serviceProvider;

    public EditorDockFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public override IRootDock CreateLayout()
    {
        var previewVm = _serviceProvider.GetRequiredService<GamePreviewPanelViewModel>();
        var previewDocument = new Document
        {
            Id = "GamePreview",
            Title = "Game Preview",
            Context = previewVm,
            CanFloat = true,
            CanClose = true,
            CanPin = false
        };

        var documentDock = new DocumentDock
        {
            Id = "Documents",
            Title = "Documents",
            IsCollapsable = false,
            ActiveDockable = previewDocument,
            VisibleDockables = CreateList<IDockable>([previewDocument]),
            EnableGlobalDocking = true
        };

        var rootDock = CreateRootDock();
        rootDock.Id = "Root";
        rootDock.IsCollapsable = false;
        rootDock.ActiveDockable = documentDock;
        rootDock.DefaultDockable = documentDock;
        rootDock.VisibleDockables = CreateList<IDockable>([documentDock]);
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
}
