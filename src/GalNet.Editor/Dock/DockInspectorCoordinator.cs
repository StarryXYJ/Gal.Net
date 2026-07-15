using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Dock.Model.Core;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Dock;

/// <summary>
/// 管理 Inspector 面板与选中 Dockable 之间的同步。
/// 从 EditorDockFactory 拆分出来，专注于 Inspector 生命周期。
/// </summary>
internal sealed class DockInspectorCoordinator
{
    private readonly Dictionary<IDockable, IDockPanelContribution> _panelByDockable;
    private readonly Func<EditorWorkspaceViewModel?> _getWorkspace;
    private readonly List<InspectorHostViewModel> _hosts = [];
    private IDockable? _lastInspectableDockable;

    public IReadOnlyList<InspectorHostViewModel> Hosts => _hosts;

    public DockInspectorCoordinator(
        Dictionary<IDockable, IDockPanelContribution> panelByDockable,
        Func<EditorWorkspaceViewModel?> getWorkspace)
    {
        _panelByDockable = panelByDockable;
        _getWorkspace = getWorkspace;
    }

    public void Clear()
    {
        foreach (var inspector in _hosts)
        {
            inspector.PropertyChanged -= OnInspectorHostPropertyChanged;
            inspector.Dispose();
        }
        _hosts.Clear();
        _lastInspectableDockable = null;
    }

    public void Register(InspectorHostViewModel inspector)
    {
        _hosts.Add(inspector);
        inspector.PropertyChanged += OnInspectorHostPropertyChanged;
    }

    private void Unregister(InspectorHostViewModel inspector)
    {
        inspector.PropertyChanged -= OnInspectorHostPropertyChanged;
        _hosts.Remove(inspector);
    }

    public void RemoveInspector(IDockable document)
    {
        if (document.Context is InspectorHostViewModel inspector)
        {
            Unregister(inspector);
            inspector.Dispose();
        }
    }

    public void RemoveInspectorAndRecover(IDockable document, Func<IDockable?> findActiveInspectable)
    {
        if (document.Context is InspectorHostViewModel inspector)
        {
            Unregister(inspector);
            inspector.Dispose();
        }
        if (ReferenceEquals(_lastInspectableDockable, document))
            _lastInspectableDockable = findActiveInspectable();
    }

    public void UpdateFor(IDockable? dockable)
    {
        if (dockable is null || !_panelByDockable.TryGetValue(dockable, out var panel))
            return;

        if (panel.PanelId == EditorDockPanelIds.Inspector)
            return;

        _lastInspectableDockable = dockable;
        foreach (var inspector in _hosts)
            UpdateSingle(inspector, dockable, isInitialTarget: false);
    }

    public void InitializeFor(InspectorHostViewModel inspector, Func<IDockable?> findActiveInspectable)
    {
        var target = _lastInspectableDockable ?? findActiveInspectable();
        if (target is not null)
            UpdateSingle(inspector, target, isInitialTarget: true);
    }

    private void UpdateSingle(InspectorHostViewModel inspector, IDockable dockable, bool isInitialTarget)
    {
        if (!_panelByDockable.TryGetValue(dockable, out var panel) || panel.PanelId == EditorDockPanelIds.Inspector)
            return;
        if (panel.Inspector is null || dockable.Context is null)
        {
            if (!isInitialTarget) inspector.FollowNoInspector();
            return;
        }

        if (isInitialTarget) inspector.SetInitialTarget(panel.PanelId, dockable.Context);
        else inspector.FollowInspectorFor(panel.PanelId, dockable.Context);
    }

    private void OnInspectorHostPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InspectorHostViewModel.IsLocked) || sender is not InspectorHostViewModel inspector || inspector.IsLocked)
            return;

        if (_lastInspectableDockable is { } target)
            UpdateSingle(inspector, target, isInitialTarget: false);
    }

    public void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e, Func<string?, IDockable?> findDockable)
    {
        var panelId = e.PropertyName switch
        {
            nameof(EditorWorkspaceViewModel.SelectedNode) or nameof(EditorWorkspaceViewModel.SelectedEdge) => EditorDockPanelIds.NodeGraph,
            nameof(EditorWorkspaceViewModel.SelectedAsset) => EditorDockPanelIds.Assets,
            _ => null
        };

        if (panelId is null) return;

        var dockable = findDockable(panelId);
        if (dockable is not null)
            UpdateFor(dockable);
    }
}
