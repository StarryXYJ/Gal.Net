using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GalNet.Core.Scene;

namespace GalNet.Game.Controls;

/// <summary>Logical game canvas that renders resource-backed scene layers in stable z order.</summary>
public class SceneLayerHost : Canvas
{
    public static readonly StyledProperty<IEnumerable<SceneLayerItem>?> ItemsSourceProperty =
        AvaloniaProperty.Register<SceneLayerHost, IEnumerable<SceneLayerItem>?>(nameof(ItemsSource));

    private readonly Dictionary<SceneLayerItem, LayerPresenter> _presenters = [];
    private readonly Dictionary<SceneLayerItem, long> _insertionOrder = [];
    private INotifyCollectionChanged? _collection;
    private long _nextInsertionOrder;

    static SceneLayerHost()
    {
        ItemsSourceProperty.Changed.AddClassHandler<SceneLayerHost>((host, _) => host.ResetItems());
    }

    public IEnumerable<SceneLayerItem>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        foreach (var (item, presenter) in _presenters) Apply(item, presenter);
        return result;
    }

    private void ResetItems()
    {
        if (_collection is not null) _collection.CollectionChanged -= OnCollectionChanged;
        foreach (var item in _presenters.Keys.ToArray()) RemoveItem(item);

        _collection = ItemsSource as INotifyCollectionChanged;
        if (_collection is not null) _collection.CollectionChanged += OnCollectionChanged;
        if (ItemsSource is null) return;
        foreach (var item in ItemsSource) AddItem(item);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.Action == NotifyCollectionChangedAction.Reset) { ResetItems(); return; }
        if (eventArgs.OldItems is not null)
            foreach (var item in eventArgs.OldItems.OfType<SceneLayerItem>()) RemoveItem(item);
        if (eventArgs.NewItems is not null)
            foreach (var item in eventArgs.NewItems.OfType<SceneLayerItem>()) AddItem(item);
    }

    private void AddItem(SceneLayerItem item)
    {
        if (_presenters.ContainsKey(item)) return;
        var presenter = new LayerPresenter();
        _presenters.Add(item, presenter);
        _insertionOrder.Add(item, _nextInsertionOrder++);
        item.PropertyChanged += OnItemPropertyChanged;
        Apply(item, presenter);
        Children.Add(presenter);
        ReorderPresenters();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is SceneLayerItem item && _presenters.TryGetValue(item, out var presenter))
        {
            if (eventArgs.PropertyName == nameof(SceneLayerItem.BlindsProgress))
            {
                // This changes every animation frame. Rebuilding the Image child here caused
                // avoidable allocations and a visible hitch when a blinds transition began.
                presenter.UpdateBlindsClip(item, Bounds.Size);
                return;
            }
            Apply(item, presenter);
            ReorderPresenters();
        }
    }

    private void RemoveItem(SceneLayerItem item)
    {
        if (!_presenters.Remove(item, out var presenter)) return;
        _insertionOrder.Remove(item);
        item.PropertyChanged -= OnItemPropertyChanged;
        Children.Remove(presenter);
    }

    private void Apply(SceneLayerItem item, LayerPresenter presenter)
    {
        presenter.Update(item, Bounds.Size);
        presenter.IsVisible = item.IsVisible;
        presenter.Opacity = item.Opacity;
        SetLeft(presenter, 0);
        SetTop(presenter, 0);
        presenter.SetValue(ZIndexProperty, (int)item.Z);
    }

    private void ReorderPresenters()
    {
        var ordered = _presenters
            .OrderBy(pair => pair.Key.Z)
            .ThenBy(pair => _insertionOrder[pair.Key])
            .Select(pair => pair.Value)
            .ToArray();
        Children.Clear();
        foreach (var presenter in ordered) Children.Add(presenter);
    }
}

/// <summary>Bindable state passed from the shared game page to <see cref="SceneLayerHost"/>.</summary>
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

internal sealed class LayerPresenter : Border
{
    public void Update(SceneLayerItem item, Size surface)
    {
        Width = Math.Max(0, surface.Width);
        Height = Math.Max(0, surface.Height);
        ClipToBounds = true;
        UpdateBlindsClip(item, surface);
        RenderTransformOrigin = RelativePoint.Center;

        if (!string.IsNullOrWhiteSpace(item.Color))
        {
            Background = new SolidColorBrush(Color.Parse(item.Color));
            Child = null;
            RenderTransform = CreateTransform(item, item.ScaleX, item.ScaleY);
            return;
        }

        var source = item.Image;
        if (source is null)
        {
            Background = new SolidColorBrush(Color.Parse("#662A2D42"));
            Child = new TextBlock { Text = "Missing layer", HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
            RenderTransform = CreateTransform(item, 1, 1);
            return;
        }

        Background = null;
        var image = new Image { Source = source, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
        var sourceWidth = Math.Max(1, source.Size.Width * item.ScaleX);
        var sourceHeight = Math.Max(1, source.Size.Height * item.ScaleY);

        switch (item.DisplayMode)
        {
            case LayerDisplayMode.Native:
                image.Width = sourceWidth;
                image.Height = sourceHeight;
                image.Stretch = Stretch.Fill;
                RenderTransform = CreateTransform(item, 1, 1);
                Child = image;
                break;
            case LayerDisplayMode.Tile:
                Child = null;
                Background = source is IImageBrushSource brushSource
                    ? new ImageBrush
                    {
                        Source = brushSource,
                        // The default destination rect is the whole surface, which produces one
                        // image plus letterboxing. A native-size absolute tile rect makes TileMode
                        // repeat the image over the complete layer surface.
                        DestinationRect = new RelativeRect(new Rect(0, 0, sourceWidth, sourceHeight), RelativeUnit.Absolute),
                        Stretch = Stretch.Fill,
                        TileMode = TileMode.Tile
                    }
                    : new SolidColorBrush(Color.Parse("#662A2D42"));
                RenderTransform = CreateTransform(item, 1, 1);
                break;
            case LayerDisplayMode.Fill:
                image.Width = surface.Width;
                image.Height = surface.Height;
                image.Stretch = Stretch.Fill;
                RenderTransform = CreateTransform(item, item.ScaleX, item.ScaleY);
                Child = image;
                break;
            case LayerDisplayMode.Uniform:
                SetContainedSize(image, sourceWidth, sourceHeight, surface, false);
                RenderTransform = CreateTransform(item, 1, 1);
                Child = image;
                break;
            case LayerDisplayMode.UniformToFill:
                SetContainedSize(image, sourceWidth, sourceHeight, surface, true);
                RenderTransform = CreateTransform(item, 1, 1);
                Child = image;
                break;
        }
    }

    public void UpdateBlindsClip(SceneLayerItem item, Size surface) => Clip = CreateBlindsClip(item, surface);

    private static Geometry? CreateBlindsClip(SceneLayerItem item, Size surface)
    {
        if (item.BlindsBladeCount <= 0) return null;
        var blades = Math.Max(1, item.BlindsBladeCount);
        var progress = Math.Clamp(item.BlindsProgress, 0, 1);
        var group = new GeometryGroup();
        if (item.BlindsHorizontal)
        {
            var height = surface.Height / blades;
            for (var index = 0; index < blades; index++)
                group.Children.Add(new RectangleGeometry(new Rect(0, index * height, surface.Width, height * progress)));
        }
        else
        {
            var width = surface.Width / blades;
            for (var index = 0; index < blades; index++)
                group.Children.Add(new RectangleGeometry(new Rect(index * width, 0, width * progress, surface.Height)));
        }
        return group;
    }

    private static void SetContainedSize(Image image, double width, double height, Size surface, bool fill)
    {
        var scale = fill
            ? Math.Max(surface.Width / width, surface.Height / height)
            : Math.Min(surface.Width / width, surface.Height / height);
        image.Width = width * scale;
        image.Height = height * scale;
        image.Stretch = Stretch.Fill;
    }

    private static Transform CreateTransform(SceneLayerItem item, double scaleX, double scaleY) => new TransformGroup
    {
        Children = [
            new ScaleTransform(scaleX, scaleY),
            new RotateTransform(item.RotationDegrees),
            new TranslateTransform(item.X, item.Y)
        ]
    };
}
