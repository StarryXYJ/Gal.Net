using GalNet.Editor.Abstraction.Extensibility;
using System.Collections.Generic;

namespace GalNet.Editor.Services;

public sealed class EditorExtensionRegistry : IEditorExtensionRegistry
{
    private readonly List<IInspectorContribution> _inspectors = [];
    private readonly List<IDockPanelContribution> _dockPanels = [];
    public IEnumerable<IInspectorContribution> InspectorContributions => _inspectors;
    public IEnumerable<IDockPanelContribution> DockPanelContributions => _dockPanels;
    public void RegisterInspector(IInspectorContribution contribution) => _inspectors.Add(contribution);
    public void RegisterDockPanel(IDockPanelContribution contribution) => _dockPanels.Add(contribution);
}
