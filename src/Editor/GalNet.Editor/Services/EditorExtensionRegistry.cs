using GalNet.Editor.Abstraction.Extensibility;
using System;
using System.Collections.Generic;

namespace GalNet.Editor.Services;

public sealed class EditorExtensionRegistry : IEditorExtensionRegistry
{
    private readonly List<IDockPanelContribution> _dockPanels = [];
    public IEnumerable<IDockPanelContribution> DockPanelContributions => _dockPanels;
    public void RegisterDockPanel(IDockPanelContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        if (string.IsNullOrWhiteSpace(contribution.PanelId))
            throw new ArgumentException("Dock panel id cannot be empty.", nameof(contribution));
        if (_dockPanels.Exists(panel => string.Equals(panel.PanelId, contribution.PanelId, StringComparison.Ordinal)))
            throw new InvalidOperationException($"A dock panel with id '{contribution.PanelId}' is already registered.");
        _dockPanels.Add(contribution);
    }

    public IDockPanelContribution? FindDockPanel(string panelId) =>
        _dockPanels.Find(panel => string.Equals(panel.PanelId, panelId, StringComparison.Ordinal));
}
