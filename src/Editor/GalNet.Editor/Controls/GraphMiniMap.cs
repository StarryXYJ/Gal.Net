using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GalNet.Editor.Models.Graph;

namespace GalNet.Editor.Controls;

/// <summary>Read-only graph overview; clicking changes only the viewport.</summary>
public sealed class GraphMiniMap : Control
{
    public static readonly StyledProperty<IEnumerable<GraphNode>?> NodesProperty = AvaloniaProperty.Register<GraphMiniMap, IEnumerable<GraphNode>?>(nameof(Nodes));
    public static readonly StyledProperty<PanZoomCanvas?> ViewportProperty = AvaloniaProperty.Register<GraphMiniMap, PanZoomCanvas?>(nameof(Viewport));
    public IEnumerable<GraphNode>? Nodes { get => GetValue(NodesProperty); set => SetValue(NodesProperty, value); }
    public PanZoomCanvas? Viewport { get => GetValue(ViewportProperty); set => SetValue(ViewportProperty, value); }
    private readonly HashSet<GraphNode> _subscribed = [];
    private INotifyCollectionChanged? _collection;
    private PanZoomCanvas? _viewport;
    private Rect _world;
    private double _scale;
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NodesProperty || change.Property == ViewportProperty) Connect();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args) { base.OnAttachedToVisualTree(args); Connect(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args) { Disconnect(); base.OnDetachedFromVisualTree(args); }
    private void Disconnect()
    {
        if (_collection is not null) _collection.CollectionChanged -= CollectionChanged;
        if (_viewport is not null) _viewport.ViewChanged -= ViewChanged;
        foreach (var node in _subscribed) node.PropertyChanged -= NodeChanged;
        _subscribed.Clear(); _collection = null; _viewport = null;
    }
    private void Connect()
    {
        Disconnect(); _collection = Nodes as INotifyCollectionChanged; _viewport = Viewport;
        if (_collection is not null) _collection.CollectionChanged += CollectionChanged;
        if (_viewport is not null) _viewport.ViewChanged += ViewChanged;
        foreach (var node in Nodes ?? []) { _subscribed.Add(node); node.PropertyChanged += NodeChanged; }
        InvalidateVisual();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => Connect();
    private void ViewChanged(object? sender, EventArgs args) => InvalidateVisual();
    private void NodeChanged(object? sender, PropertyChangedEventArgs args) => InvalidateVisual();
    private IBrush ThemeBrush(string key, IBrush fallback) => this.TryFindResource(key, out var value) && value is IBrush brush ? brush : fallback;
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(ThemeBrush("SemiColorBackground1", Brushes.Black), new Pen(ThemeBrush("SemiColorBorder", Brushes.Gray)), new Rect(Bounds.Size));
        var nodes = Nodes?.ToArray() ?? [];
        if (nodes.Length == 0) return;
        var x = nodes.Min(node => node.X); var y = nodes.Min(node => node.Y);
        _world = new Rect(x, y, Math.Max(1, nodes.Max(node => node.X) - x + 240), Math.Max(1, nodes.Max(node => node.Y) - y + 100));
        _scale = Math.Min(Math.Max(1, Bounds.Width - 12) / _world.Width, Math.Max(1, Bounds.Height - 12) / _world.Height);
        foreach (var node in nodes)
            context.DrawRectangle(node.IsSelected ? ThemeBrush("SemiColorPrimary", Brushes.MediumPurple) : ThemeBrush("SemiColorText2", Brushes.SlateGray), null, new Rect(6 + (node.X - x) * _scale, 6 + (node.Y - y) * _scale, 200 * _scale, 70 * _scale));
        if (Viewport is { } viewport)
        {
            var top = viewport.ViewportToWorld(default); var bottom = viewport.ViewportToWorld(new Point(viewport.Bounds.Width, viewport.Bounds.Height));
            using (context.PushClip(new Rect(Bounds.Size)))
                context.DrawRectangle(null, new Pen(ThemeBrush("SemiColorText0", Brushes.White), 1), new Rect(6 + (top.X - x) * _scale, 6 + (top.Y - y) * _scale, (bottom.X - top.X) * _scale, (bottom.Y - top.Y) * _scale));
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs args)
    {
        base.OnPointerPressed(args);
        if (_scale <= 0 || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = args.GetPosition(this); Viewport?.CenterOn(new Point(_world.X + (point.X - 6) / _scale, _world.Y + (point.Y - 6) / _scale)); args.Handled = true;
    }
}
