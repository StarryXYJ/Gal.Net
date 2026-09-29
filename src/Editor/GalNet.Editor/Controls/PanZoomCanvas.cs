using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GalNet.Editor.Controls;

/// <summary>
/// Hosts a large canvas-like child with graph-editor style panning and
/// Photoshop/SAI style mouse-anchored zooming.
/// </summary>
public class PanZoomCanvas : Decorator
{
    private const double DefaultZoomStep = 1.15;
    private const double ZoomEpsilon = 0.0001;

    private double _offsetX;
    private double _offsetY;
    private double _scale = 1.0;
    private readonly MatrixTransform _viewportTransform = new();

    private Point _lastMousePos;
    private bool _isPanning;
    private bool _isViewInitialized;

    public static readonly StyledProperty<double> CanvasOriginXProperty =
        AvaloniaProperty.Register<PanZoomCanvas, double>(nameof(CanvasOriginX), 0.0);

    public static readonly StyledProperty<double> CanvasOriginYProperty =
        AvaloniaProperty.Register<PanZoomCanvas, double>(nameof(CanvasOriginY), 0.0);

    public static readonly StyledProperty<double> CanvasWidthProperty =
        AvaloniaProperty.Register<PanZoomCanvas, double>(nameof(CanvasWidth), 5000.0);

    public static readonly StyledProperty<double> CanvasHeightProperty =
        AvaloniaProperty.Register<PanZoomCanvas, double>(nameof(CanvasHeight), 5000.0);

    public static readonly StyledProperty<double> MinZoomProperty =
        AvaloniaProperty.Register<PanZoomCanvas, double>(nameof(MinZoom), 0.1);

    public static readonly StyledProperty<double> MaxZoomProperty =
        AvaloniaProperty.Register<PanZoomCanvas, double>(nameof(MaxZoom), 10.0);

    public static readonly StyledProperty<double> ZoomStepProperty =
        AvaloniaProperty.Register<PanZoomCanvas, double>(nameof(ZoomStep), DefaultZoomStep);

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<PanZoomCanvas, IBrush?>(nameof(Background));

    public double CanvasOriginX
    {
        get => GetValue(CanvasOriginXProperty);
        set => SetValue(CanvasOriginXProperty, value);
    }

    public double CanvasOriginY
    {
        get => GetValue(CanvasOriginYProperty);
        set => SetValue(CanvasOriginYProperty, value);
    }

    public double CanvasWidth
    {
        get => GetValue(CanvasWidthProperty);
        set => SetValue(CanvasWidthProperty, value);
    }

    public double CanvasHeight
    {
        get => GetValue(CanvasHeightProperty);
        set => SetValue(CanvasHeightProperty, value);
    }

    public double MinZoom
    {
        get => GetValue(MinZoomProperty);
        set => SetValue(MinZoomProperty, value);
    }

    public double MaxZoom
    {
        get => GetValue(MaxZoomProperty);
        set => SetValue(MaxZoomProperty, value);
    }

    public double ZoomStep
    {
        get => GetValue(ZoomStepProperty);
        set => SetValue(ZoomStepProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public double Zoom => _scale;

    public double OffsetX => _offsetX;

    public double OffsetY => _offsetY;

    public event EventHandler? ViewChanged;

    public PanZoomCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Background is { } background)
            context.FillRectangle(background, Bounds);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty ||
            change.Property == CanvasOriginXProperty ||
            change.Property == CanvasOriginYProperty ||
            change.Property == CanvasWidthProperty ||
            change.Property == CanvasHeightProperty ||
            change.Property == MinZoomProperty ||
            change.Property == MaxZoomProperty)
        {
            CoerceViewport();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(new Size(CanvasWidth, CanvasHeight));
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));

        if (finalSize.Width > 0 && finalSize.Height > 0)
            EnsureViewInitialized(finalSize);
        else
            UpdateTransform(false);

        return finalSize;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.MiddleButtonPressed)
            return;

        EnsureViewInitialized(Bounds.Size);

        _isPanning = true;
        _lastMousePos = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.MiddleButtonReleased)
            return;

        _isPanning = false;
        e.Pointer.Capture(null);
        ViewChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isPanning)
            return;

        var currentMousePos = e.GetPosition(this);
        var delta = currentMousePos - _lastMousePos;

        _offsetX += delta.X;
        _offsetY += delta.Y;

        UpdateTransform(false);

        _lastMousePos = currentMousePos;
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        EnsureViewInitialized(Bounds.Size);

        var mousePosition = e.GetPosition(this);

        var wheelDirection = Math.Sign(e.Delta.Y);
        if (wheelDirection == 0)
            return;

        var zoomFactor = wheelDirection > 0 ? Math.Max(1.001, ZoomStep) : 1.0 / Math.Max(1.001, ZoomStep);
        var newScale = Math.Clamp(_scale * zoomFactor, MinZoom, MaxZoom);

        if (Math.Abs(newScale - _scale) < ZoomEpsilon)
            return;

        ZoomAt(mousePosition, newScale);

        UpdateTransform();

        e.Handled = true;
    }

    public Point ViewportToWorld(Point viewportPoint) => ScreenToWorld(viewportPoint, _scale);

    public Point WorldToViewport(Point worldPoint)
    {
        return new Point(
            worldPoint.X * _scale + _offsetX,
            worldPoint.Y * _scale + _offsetY);
    }

    public void SetViewport(double zoom, double offsetX, double offsetY)
    {
        _scale = Math.Clamp(zoom, MinZoom, MaxZoom);
        _offsetX = offsetX;
        _offsetY = offsetY;
        _isViewInitialized = true;
        UpdateTransform();
    }

    public void CenterOnWorldBounds(double zoom)
    {
        _scale = Math.Clamp(zoom, MinZoom, MaxZoom);
        var worldBounds = GetWorldBounds();
        CenterOn(worldBounds.Center);
    }

    public void CenterOn(Point worldPoint)
    {
        _offsetX = Bounds.Width / 2.0 - worldPoint.X * _scale;
        _offsetY = Bounds.Height / 2.0 - worldPoint.Y * _scale;
        _isViewInitialized = true;
        UpdateTransform();
    }

    private void EnsureViewInitialized(Size viewportSize)
    {
        if (_isViewInitialized)
            return;

        _scale = Math.Clamp(_scale, MinZoom, MaxZoom);

        var worldBounds = GetWorldBounds();
        var viewportCenter = new Point(viewportSize.Width / 2.0, viewportSize.Height / 2.0);
        var worldCenter = worldBounds.Center;

        _offsetX = viewportCenter.X - worldCenter.X * _scale;
        _offsetY = viewportCenter.Y - worldCenter.Y * _scale;
        _isViewInitialized = true;

        UpdateTransform(false);
    }

    private void CoerceViewport()
    {
        if (!_isViewInitialized)
            return;

        var oldScale = _scale;
        _scale = Math.Clamp(_scale, MinZoom, MaxZoom);

        if (Math.Abs(oldScale - _scale) > ZoomEpsilon)
        {
            var viewportCenter = new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);
            var worldAtCenter = ScreenToWorld(viewportCenter, oldScale);
            _offsetX = viewportCenter.X - worldAtCenter.X * _scale;
            _offsetY = viewportCenter.Y - worldAtCenter.Y * _scale;
        }

        UpdateTransform();
    }

    private void ZoomAt(Point anchor, double newScale)
    {
        var zoomFactor = newScale / _scale;

        _offsetX = anchor.X - (anchor.X - _offsetX) * zoomFactor;
        _offsetY = anchor.Y - (anchor.Y - _offsetY) * zoomFactor;
        _scale = newScale;
    }

    private Point ScreenToWorld(Point screenPoint, double scale)
    {
        if (scale <= 0)
            return default;

        return new Point(
            (screenPoint.X - _offsetX) / scale,
            (screenPoint.Y - _offsetY) / scale);
    }

    private Rect GetWorldBounds()
    {
        return new Rect(CanvasOriginX, CanvasOriginY, Math.Max(0, CanvasWidth), Math.Max(0, CanvasHeight));
    }

    private void CoerceOffset()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var worldBounds = GetWorldBounds();
        var scaledWidth = worldBounds.Width * _scale;
        var scaledHeight = worldBounds.Height * _scale;

        _offsetX = CoerceAxis(
            _offsetX,
            Bounds.Width,
            scaledWidth,
            worldBounds.Left,
            worldBounds.Right);
        _offsetY = CoerceAxis(
            _offsetY,
            Bounds.Height,
            scaledHeight,
            worldBounds.Top,
            worldBounds.Bottom);
    }

    private double CoerceAxis(double offset, double viewportSize, double scaledContentSize, double worldStart, double worldEnd)
    {
        if (viewportSize <= 0 || scaledContentSize <= 0)
            return offset;

        if (scaledContentSize <= viewportSize)
            return viewportSize / 2.0 - ((worldStart + worldEnd) / 2.0) * _scale;

        var minOffset = viewportSize - worldEnd * _scale;
        var maxOffset = -worldStart * _scale;
        return Math.Clamp(offset, minOffset, maxOffset);
    }

    private void UpdateTransform(bool notify = true)
    {
        if (Child is null)
            return;

        CoerceOffset();

        Child.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        _viewportTransform.Matrix = new Matrix(_scale, 0, 0, _scale, _offsetX, _offsetY);
        Child.RenderTransform = _viewportTransform;

        if (notify)
            ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
