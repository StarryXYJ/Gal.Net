using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GalNet.Core.Scene;

namespace GalNet.Game.Controls;

/// <summary>
/// The single presentation surface for the game scene. Layers are render data, not
/// child controls: this host builds a stable render plan and submits it to one drawing
/// context. A future GPU render graph consumes the same plan before this control
/// presents the final scene texture.
/// </summary>
public sealed class SceneLayerHost : Control
{
    public static readonly StyledProperty<IEnumerable<SceneLayerItem>?> ItemsSourceProperty =
        AvaloniaProperty.Register<SceneLayerHost, IEnumerable<SceneLayerItem>?>(nameof(ItemsSource));

    private readonly Dictionary<SceneLayerItem, long> _insertionOrder = [];
    private INotifyCollectionChanged? _collection;
    private long _nextInsertionOrder;
    private SceneRenderPlan _renderPlan = SceneRenderPlan.Empty;

    static SceneLayerHost()
    {
        ItemsSourceProperty.Changed.AddClassHandler<SceneLayerHost>((host, _) => host.ResetItems());
    }

    public IEnumerable<SceneLayerItem>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Stable scene ordering consumed by the current presenter and the future GPU backend.</summary>
    public SceneRenderPlan RenderPlan => _renderPlan;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        foreach (var item in _renderPlan.Items)
            SceneLayerRenderer.Render(context, item.Layer, Bounds.Size);
    }

    private void ResetItems()
    {
        if (_collection is not null) _collection.CollectionChanged -= OnCollectionChanged;
        foreach (var item in _insertionOrder.Keys) item.PropertyChanged -= OnItemPropertyChanged;
        _insertionOrder.Clear();

        _collection = ItemsSource as INotifyCollectionChanged;
        if (_collection is not null) _collection.CollectionChanged += OnCollectionChanged;
        if (ItemsSource is not null)
            foreach (var item in ItemsSource) AddItem(item);
        RebuildRenderPlan();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.Action == NotifyCollectionChangedAction.Reset) { ResetItems(); return; }
        if (eventArgs.OldItems is not null)
            foreach (var item in eventArgs.OldItems.OfType<SceneLayerItem>()) RemoveItem(item);
        if (eventArgs.NewItems is not null)
            foreach (var item in eventArgs.NewItems.OfType<SceneLayerItem>()) AddItem(item);
        RebuildRenderPlan();
    }

    private void AddItem(SceneLayerItem item)
    {
        if (!_insertionOrder.TryAdd(item, _nextInsertionOrder++)) return;
        item.PropertyChanged += OnItemPropertyChanged;
    }

    private void RemoveItem(SceneLayerItem item)
    {
        if (!_insertionOrder.Remove(item)) return;
        item.PropertyChanged -= OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is not SceneLayerItem item || !_insertionOrder.ContainsKey(item)) return;
        if (eventArgs.PropertyName == nameof(SceneLayerItem.Z)) RebuildRenderPlan();
        else InvalidateVisual();
    }

    private void RebuildRenderPlan()
    {
        _renderPlan = SceneRenderPlan.Create(_insertionOrder.Select(pair => new SceneRenderEntry(pair.Key, pair.Value)));
        InvalidateVisual();
    }
}

/// <summary>One stable, backend-agnostic scene draw entry.</summary>
public sealed record SceneRenderEntry(SceneLayerItem Layer, long InsertionOrder)
{
    public double Order => Layer.Z;
}

/// <summary>Ordered Layer input to scene composition. Scene objects join this plan in Phase 2's next slice.</summary>
public sealed class SceneRenderPlan
{
    public static SceneRenderPlan Empty { get; } = new([]);
    public IReadOnlyList<SceneRenderEntry> Items { get; }

    private SceneRenderPlan(IReadOnlyList<SceneRenderEntry> items) => Items = items;

    public static SceneRenderPlan Create(IEnumerable<SceneRenderEntry> entries) => new(entries
        .OrderBy(entry => entry.Order)
        .ThenBy(entry => entry.InsertionOrder)
        .ToArray());
}

/// <summary>Temporary Avalonia draw backend. It is intentionally the only location that knows Avalonia drawing APIs.</summary>
internal static class SceneLayerRenderer
{
    private static readonly IBrush MissingLayerBrush = new SolidColorBrush(Color.Parse("#662A2D42"));

    public static void Render(DrawingContext context, SceneLayerItem item, Size surface)
    {
        if (!item.IsVisible || item.Opacity <= 0 || surface.Width <= 0 || surface.Height <= 0) return;
        using var opacity = context.PushOpacity(Math.Clamp(item.Opacity, 0, 1));
        if (!string.IsNullOrWhiteSpace(item.Color))
        {
            using var colorTransform = context.PushTransform(CreateTransform(item, surface, item.ScaleX, item.ScaleY));
            context.DrawRectangle(new SolidColorBrush(Color.Parse(item.Color)), null, new Rect(surface));
            return;
        }

        if (item.Image is null)
        {
            context.DrawRectangle(MissingLayerBrush, null, new Rect(surface));
            return;
        }

        var (source, sourceSize) = GetSource(item.Image, item.Flipbook);
        var (destination, transformScaleX, transformScaleY) = GetDestination(item, sourceSize, surface);
        using var transform = context.PushTransform(CreateTransform(item, surface, transformScaleX, transformScaleY));

        if (item.DisplayMode == LayerDisplayMode.Tile)
        {
            DrawTiled(context, item, source, sourceSize, surface);
            return;
        }

        DrawMasked(context, item, source, destination, surface);
    }

    private static (Rect Source, Size Size) GetSource(IImage image, FlipbookDefinition? flipbook)
    {
        if (flipbook is not { IsValid: true }) return (new Rect(image.Size), image.Size);
        var width = image.Size.Width / flipbook.Columns;
        var height = image.Size.Height / flipbook.Rows;
        var index = flipbook.CurrentFrameIndex;
        return (new Rect((index % flipbook.Columns) * width, (index / flipbook.Columns) * height, width, height), new Size(width, height));
    }

    private static (Rect Destination, double TransformScaleX, double TransformScaleY) GetDestination(SceneLayerItem item, Size source, Size surface)
    {
        var native = new Size(Math.Max(1, source.Width * item.ScaleX), Math.Max(1, source.Height * item.ScaleY));
        return item.DisplayMode switch
        {
            LayerDisplayMode.Native => (Center(native, surface), 1, 1),
            LayerDisplayMode.Fill => (new Rect(surface), item.ScaleX, item.ScaleY),
            LayerDisplayMode.Uniform => (Contain(native, surface, false), 1, 1),
            LayerDisplayMode.UniformToFill => (Contain(native, surface, true), 1, 1),
            LayerDisplayMode.Tile => (new Rect(surface), 1, 1),
            _ => (new Rect(surface), 1, 1)
        };
    }

    private static Rect Center(Size size, Size surface) => new((surface.Width - size.Width) / 2, (surface.Height - size.Height) / 2, size.Width, size.Height);

    private static Rect Contain(Size source, Size surface, bool fill)
    {
        var scale = fill
            ? Math.Max(surface.Width / source.Width, surface.Height / source.Height)
            : Math.Min(surface.Width / source.Width, surface.Height / source.Height);
        return Center(new Size(source.Width * scale, source.Height * scale), surface);
    }

    private static void DrawTiled(DrawingContext context, SceneLayerItem item, Rect source, Size sourceSize, Size surface)
    {
        var width = Math.Max(1, sourceSize.Width * item.ScaleX);
        var height = Math.Max(1, sourceSize.Height * item.ScaleY);
        for (var y = 0d; y < surface.Height; y += height)
            for (var x = 0d; x < surface.Width; x += width)
                context.DrawImage(item.Image!, source, new Rect(x, y, width, height));
    }

    private static void DrawMasked(DrawingContext context, SceneLayerItem item, Rect source, Rect destination, Size surface)
    {
        if (item.BlindsBladeCount <= 0)
        {
            context.DrawImage(item.Image!, source, destination);
            return;
        }

        var blades = Math.Max(1, item.BlindsBladeCount);
        var progress = Math.Clamp(item.BlindsProgress, 0, 1);
        for (var index = 0; index < blades; index++)
        {
            Rect clip;
            if (item.BlindsHorizontal)
            {
                var height = surface.Height / blades;
                clip = new Rect(0, index * height, surface.Width, height * progress);
            }
            else
            {
                var width = surface.Width / blades;
                clip = new Rect(index * width, 0, width * progress, surface.Height);
            }
            using (context.PushClip(clip)) context.DrawImage(item.Image!, source, destination);
        }
    }

    private static Matrix CreateTransform(SceneLayerItem item, Size surface, double scaleX, double scaleY)
    {
        var radians = item.RotationDegrees * Math.PI / 180;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var m11 = scaleX * cosine;
        var m12 = scaleX * sine;
        var m21 = -scaleY * sine;
        var m22 = scaleY * cosine;
        var centerX = surface.Width / 2;
        var centerY = surface.Height / 2;
        return new Matrix(m11, m12, m21, m22,
            centerX + item.X - ((centerX * m11) + (centerY * m21)),
            centerY + item.Y - ((centerX * m12) + (centerY * m22)));
    }
}

/// <summary>Bindable Layer render data passed from the shared game page to <see cref="SceneLayerHost"/>.</summary>
public sealed class SceneLayerItem : INotifyPropertyChanged
{
    private string _handleId = string.Empty;
    private IImage? _image;
    private string? _color;
    private double _x;
    private double _y;
    private double _rotationDegrees;
    private double _scaleX = 1;
    private double _scaleY = 1;
    private double _z;
    private LayerDisplayMode _displayMode;
    private double _opacity = 1;
    private bool _isVisible = true;
    private double _blindsProgress = 1;
    private int _blindsBladeCount;
    private bool _blindsHorizontal;
    private FlipbookDefinition? _flipbook;

    public string HandleId { get => _handleId; set => SetField(ref _handleId, value); }
    public IImage? Image { get => _image; set => SetField(ref _image, value); }
    public string? Color { get => _color; set => SetField(ref _color, value); }
    public double X { get => _x; set => SetField(ref _x, value); }
    public double Y { get => _y; set => SetField(ref _y, value); }
    public double RotationDegrees { get => _rotationDegrees; set => SetField(ref _rotationDegrees, value); }
    public double ScaleX { get => _scaleX; set => SetField(ref _scaleX, value); }
    public double ScaleY { get => _scaleY; set => SetField(ref _scaleY, value); }
    public double Z { get => _z; set => SetField(ref _z, value); }
    public LayerDisplayMode DisplayMode { get => _displayMode; set => SetField(ref _displayMode, value); }
    public double Opacity { get => _opacity; set => SetField(ref _opacity, value); }
    public bool IsVisible { get => _isVisible; set => SetField(ref _isVisible, value); }
    /// <summary>Optional sprite-sheet layout for <see cref="Image"/>. The source remains one image while index selects its current frame.</summary>
    public FlipbookDefinition? Flipbook { get => _flipbook; set => SetField(ref _flipbook, value); }
    public double FlipbookIndex
    {
        get => _flipbook?.Index ?? 0;
        set
        {
            if (_flipbook is null || Math.Abs(_flipbook.Index - value) < double.Epsilon) return;
            _flipbook.Index = (float)Math.Max(0, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlipbookIndex)));
        }
    }
    /// <summary>0 hides a blinds-masked layer and 1 reveals it fully.</summary>
    public double BlindsProgress { get => _blindsProgress; set => SetField(ref _blindsProgress, Math.Clamp(value, 0, 1)); }
    /// <summary>Zero means no blinds mask.</summary>
    public int BlindsBladeCount { get => _blindsBladeCount; set => SetField(ref _blindsBladeCount, Math.Max(0, value)); }
    public bool BlindsHorizontal { get => _blindsHorizontal; set => SetField(ref _blindsHorizontal, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
