using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Collections;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Mvvm.Core;

namespace GalNet.Editor.Dock;

/// <summary>
/// Stores only editor-layout data and recreates Dock objects on load. Runtime references
/// such as Context, Owner, Factory, Window host and commands never enter JSON.
/// </summary>
public sealed class DockLayoutSerializer
{
    private const int FormatVersion = 1;
    // Dock uses NaN/Infinity for values which are not constrained yet (for example,
    // an unmeasured floating window). Persist them as named literals so saving a
    // layout never fails before Avalonia has assigned a concrete size or position.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
    private readonly Dictionary<IDockable, string> _keys = new(ReferenceEqualityComparer.Instance);

    public string Serialize(IRootDock layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _keys.Clear();
        var root = SaveNode(layout, "root") ?? throw new InvalidOperationException("Cannot save an empty root Dock.");
        var windows = layout.Windows?
            .Where(window => window.Layout is not null)
            .Select((window, index) => new WindowData
            {
                Id = window.Id,
                Title = window.Title,
                X = window.X,
                Y = window.Y,
                Width = window.Width,
                Height = window.Height,
                State = window.WindowState,
                Topmost = window.Topmost,
                Layout = SaveNode(window.Layout!, $"floating:{index}")
            })
            .ToList() ?? [];

        return JsonSerializer.Serialize(new LayoutData { Version = FormatVersion, Root = root, Windows = windows }, JsonOptions);
    }

    public IRootDock? Deserialize(string serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
            return null;

        using var document = JsonDocument.Parse(serialized);
        // Reject the old Dock.Serializer reference graph rather than attempting to repair it.
        if (document.RootElement.TryGetProperty("$type", out _)
            || document.RootElement.TryGetProperty("$id", out _)
            || document.RootElement.TryGetProperty("$ref", out _))
            return null;

        var data = JsonSerializer.Deserialize<LayoutData>(serialized, JsonOptions);
        if (data is not { Version: FormatVersion, Root.Type: NodeType.Root })
            return null;

        var reader = new Reader();
        var root = reader.LoadNode(data.Root) as IRootDock;
        if (root is null)
            return null;

        root.Windows = new AvaloniaList<IDockWindow>();
        foreach (var savedWindow in data.Windows)
        {
            if (savedWindow.Layout is not { Type: NodeType.Root })
                return null;
            var floatingRoot = reader.LoadNode(savedWindow.Layout) as IRootDock;
            if (floatingRoot is null)
                return null;

            var window = new DockWindow
            {
                Id = savedWindow.Id ?? "",
                Title = savedWindow.Title ?? "",
                X = savedWindow.X,
                Y = savedWindow.Y,
                Width = savedWindow.Width,
                Height = savedWindow.Height,
                WindowState = savedWindow.State,
                Topmost = savedWindow.Topmost,
                Layout = floatingRoot
            };
            floatingRoot.Window = window;
            root.Windows.Add(window);
        }

        reader.ResolveReferences();
        return root;
    }

    private NodeData? SaveNode(IDockable dockable, string key)
    {
        if (dockable is Document document && IsTransient(document))
            return null;

        _keys[dockable] = key;
        var data = new NodeData
        {
            Key = key,
            Type = GetNodeType(dockable),
            Id = dockable.Id,
            Title = dockable.Title,
            Proportion = dockable.Proportion,
            CollapsedProportion = dockable.CollapsedProportion,
            IsCollapsable = dockable.IsCollapsable,
            CanClose = dockable.CanClose,
            CanFloat = dockable.CanFloat
        };

        if (dockable is IProportionalDock proportional)
            data.Orientation = proportional.Orientation;
        if (dockable is IToolDock tool)
            data.Alignment = tool.Alignment;
        if (dockable is IDock dock)
        {
            data.EnableGlobalDocking = dock.EnableGlobalDocking;
            data.Children = SaveChildren(dock.VisibleDockables, key + ":v");
            data.ActiveKey = GetKey(dock.ActiveDockable);
            data.DefaultKey = GetKey(dock.DefaultDockable);
            data.FocusedKey = GetKey(dock.FocusedDockable);
        }
        if (dockable is IRootDock root)
        {
            data.Hidden = SaveChildren(root.HiddenDockables, key + ":h");
            data.LeftPinned = SaveChildren(root.LeftPinnedDockables, key + ":l");
            data.RightPinned = SaveChildren(root.RightPinnedDockables, key + ":r");
            data.TopPinned = SaveChildren(root.TopPinnedDockables, key + ":t");
            data.BottomPinned = SaveChildren(root.BottomPinnedDockables, key + ":b");
            data.PinnedDock = root.PinnedDock is null ? null : SaveNode(root.PinnedDock, key + ":p");
        }
        return data;
    }

    private List<NodeData> SaveChildren(IList<IDockable>? children, string keyPrefix) =>
        children?.Select((child, index) => SaveNode(child, $"{keyPrefix}:{index}"))
            .Where(child => child is not null)
            .Cast<NodeData>()
            .ToList() ?? [];

    private string? GetKey(IDockable? dockable) => dockable is not null && _keys.TryGetValue(dockable, out var key) ? key : null;

    private static bool IsTransient(Document document) =>
        document.Id?.StartsWith(EditorDockPanelIds.GroupEditor + ":", StringComparison.Ordinal) == true;

    private static NodeType GetNodeType(IDockable dockable) => dockable switch
    {
        IRootDock => NodeType.Root,
        IProportionalDock => NodeType.Proportional,
        IDocumentDock => NodeType.DocumentDock,
        IToolDock => NodeType.ToolDock,
        IProportionalDockSplitter => NodeType.Splitter,
        Document => NodeType.Document,
        _ => throw new InvalidOperationException($"Dock type '{dockable.GetType().Name}' cannot be persisted.")
    };

    private sealed class Reader
    {
        private readonly Dictionary<string, IDockable> _nodes = new(StringComparer.Ordinal);
        private readonly List<(IDock Dock, NodeData Data)> _docks = [];

        public IDockable LoadNode(NodeData data)
        {
            if (string.IsNullOrWhiteSpace(data.Key) || !_nodes.TryAdd(data.Key, null!))
                throw new InvalidOperationException("Dock layout contains duplicate node keys.");

            IDockable node = data.Type switch
            {
                NodeType.Root => new RootDock(),
                NodeType.Proportional => new ProportionalDock { Orientation = data.Orientation },
                NodeType.DocumentDock => new DocumentDock(),
                NodeType.ToolDock => new ToolDock { Alignment = data.Alignment },
                NodeType.Splitter => new ProportionalDockSplitter(),
                NodeType.Document => new Document { CanClose = data.CanClose, CanFloat = data.CanFloat },
                _ => throw new InvalidOperationException("Dock layout contains an unknown node type.")
            };
            _nodes[data.Key] = node;
            node.Id = data.Id ?? "";
            node.Title = data.Title ?? "";
            node.Proportion = data.Proportion;
            node.CollapsedProportion = data.CollapsedProportion;
            node.IsCollapsable = data.IsCollapsable;

            if (node is IDock dock)
            {
                dock.EnableGlobalDocking = data.EnableGlobalDocking;
                dock.VisibleDockables = new AvaloniaList<IDockable>(data.Children.Select(LoadNode));
                _docks.Add((dock, data));
            }
            if (node is IRootDock root)
            {
                root.HiddenDockables = new AvaloniaList<IDockable>(data.Hidden.Select(LoadNode));
                root.LeftPinnedDockables = new AvaloniaList<IDockable>(data.LeftPinned.Select(LoadNode));
                root.RightPinnedDockables = new AvaloniaList<IDockable>(data.RightPinned.Select(LoadNode));
                root.TopPinnedDockables = new AvaloniaList<IDockable>(data.TopPinned.Select(LoadNode));
                root.BottomPinnedDockables = new AvaloniaList<IDockable>(data.BottomPinned.Select(LoadNode));
                root.PinnedDock = data.PinnedDock is null ? null : LoadNode(data.PinnedDock) as IToolDock;
            }
            return node;
        }

        public void ResolveReferences()
        {
            foreach (var (dock, data) in _docks)
            {
                dock.ActiveDockable = ResolveChild(dock, data.ActiveKey, required: true);
                dock.DefaultDockable = ResolveChild(dock, data.DefaultKey, required: false);
                dock.FocusedDockable = ResolveChild(dock, data.FocusedKey, required: false);
            }
        }

        private IDockable? ResolveChild(IDock dock, string? key, bool required)
        {
            if (string.IsNullOrWhiteSpace(key))
                return dock.VisibleDockables?.FirstOrDefault(child => child is not IProportionalDockSplitter);
            if (_nodes.TryGetValue(key, out var child) && dock.VisibleDockables?.Contains(child) == true)
                return child;
            if (required)
                throw new InvalidOperationException("Dock layout has an invalid active item reference.");
            return null;
        }
    }

    private sealed class LayoutData
    {
        public int Version { get; set; }
        public NodeData Root { get; set; } = new();
        public List<WindowData> Windows { get; set; } = [];
    }

    private sealed class WindowData
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public DockWindowState State { get; set; }
        public bool Topmost { get; set; }
        public NodeData? Layout { get; set; }
    }

    private sealed class NodeData
    {
        public string Key { get; set; } = "";
        public NodeType Type { get; set; }
        public string? Id { get; set; }
        public string? Title { get; set; }
        public double Proportion { get; set; }
        public double CollapsedProportion { get; set; }
        public Orientation Orientation { get; set; }
        public Alignment Alignment { get; set; }
        public bool IsCollapsable { get; set; }
        public bool CanClose { get; set; }
        public bool CanFloat { get; set; }
        public bool EnableGlobalDocking { get; set; }
        public string? ActiveKey { get; set; }
        public string? DefaultKey { get; set; }
        public string? FocusedKey { get; set; }
        public List<NodeData> Children { get; set; } = [];
        public List<NodeData> Hidden { get; set; } = [];
        public List<NodeData> LeftPinned { get; set; } = [];
        public List<NodeData> RightPinned { get; set; } = [];
        public List<NodeData> TopPinned { get; set; } = [];
        public List<NodeData> BottomPinned { get; set; } = [];
        public NodeData? PinnedDock { get; set; }
    }

    private enum NodeType { Root, Proportional, DocumentDock, ToolDock, Splitter, Document }
}
