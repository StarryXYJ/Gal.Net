using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace GalNet.Game.Controls.Timeline;

/// <summary>Integer-frame key canvas, reusable without Editor services or history.</summary>
public sealed class TimelineCanvas : Control
{
    public static readonly StyledProperty<TimelineCanvasModel?> ModelProperty = AvaloniaProperty.Register<TimelineCanvas, TimelineCanvasModel?>(nameof(Model));
    public static readonly StyledProperty<bool> IsRulerProperty = AvaloniaProperty.Register<TimelineCanvas, bool>(nameof(IsRuler));
    public TimelineCanvasModel? Model { get => GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public bool IsRuler { get => GetValue(IsRulerProperty); set => SetValue(IsRulerProperty, value); }
    private TimelineCanvasModel? _observed;
    private Point? _origin, _boxEnd;
    private bool _keyPress, _summary, _drag, _seeking, _extend;
    private int _version;
    private Guid[] _prior = [];
    private Guid[] _pressedIds = [];
    private IPointer? _pointer;
    public TimelineCanvas() { Focusable = true; ClipToBounds = true; }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == ModelProperty) { Observe(); InvalidateVisual(); } }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Observe(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { if (_drag || _seeking) Model?.Cancel(); ReleaseLocal(); if (_observed is not null) _observed.PropertyChanged -= ModelChanged; _observed = null; base.OnDetachedFromVisualTree(e); }
    private void Observe()
    {
        if (ReferenceEquals(_observed, Model)) return;
        if (_observed is not null) { if (_drag || _seeking) _observed.Cancel(); _observed.PropertyChanged -= ModelChanged; }
        ReleaseLocal();
        _observed = Model; if (_observed is not null) _observed.PropertyChanged += ModelChanged;
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e) { if (_origin is not null && Model?.InteractionVersion != _version) ReleaseLocal(); InvalidateVisual(); }
    private double X(int frame) => Model is { } model ? (frame - model.StartFrame) * model.PixelsPerFrame : 0;
    private int FrameAt(double x) => Model is { } model ? (int)Math.Clamp(Math.Round(model.StartFrame + x / model.PixelsPerFrame, MidpointRounding.AwayFromZero), 0, model.Duration) : 0;
    public override void Render(DrawingContext context)
    {
        base.Render(context); if (Model is not { } model) return;
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        var grid = new Pen(Brushes.SlateGray, .4);
        var requested = Math.Max(1, 70 / model.PixelsPerFrame); var power = Math.Pow(10, Math.Floor(Math.Log10(requested)));
        var step = (long)(new[] { 1d, 2d, 5d, 10d }.First(value => value * power >= requested) * power);
        var last = Math.Min(model.Duration, model.StartFrame + Bounds.Width / model.PixelsPerFrame);
        for (var frame = (long)(Math.Floor(model.StartFrame / step) * step); frame <= last; frame += step)
        {
            var x = X((int)frame); context.DrawLine(grid, new Point(x, 0), new Point(x, Bounds.Height));
            if (IsRuler) context.DrawText(new FormattedText(frame.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default), 11, TextElement.GetForeground(this) ?? Brushes.Gray), new Point(x + 3, 3));
        }
        if (!IsRuler)
        {
            for (var index = 0; index < model.Rows.Count; index++)
            {
                var y = (index + .5) * model.RowHeight;
                context.DrawLine(grid, new Point(0, (index + 1) * model.RowHeight), new Point(Bounds.Width, (index + 1) * model.RowHeight));
                var row = model.Rows[index];
                foreach (var keys in row.Keys.GroupBy(key => (long)key.Frame + (model.Selection.Contains(key.Id) && model.HasPendingMove ? model.PreviewDelta : 0)))
                {
                    var selected = keys.Any(key => model.Selection.Contains(key.Id));
                    var frame = keys.Key;
                    var x = (frame - model.StartFrame) * model.PixelsPerFrame; if (x < -8 || x > Bounds.Width + 8) continue;
                    var geometry = new StreamGeometry(); using (var path = geometry.Open()) { path.BeginFigure(new Point(x, y - 6), true); path.LineTo(new Point(x + 6, y)); path.LineTo(new Point(x, y + 6)); path.LineTo(new Point(x - 6, y)); path.EndFigure(true); }
                    var brush = selected ? model.HasPendingMove && !model.IsMoveValid ? Brushes.IndianRed : Brushes.DodgerBlue : row.IsSummary ? Brushes.SlateGray : Brushes.MediumPurple;
                    context.DrawGeometry(brush, new Pen(selected ? Brushes.DodgerBlue : Brushes.Gray, 1), geometry);
                }
            }
            if (_origin is { } start && _boxEnd is { } end) context.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 1), new Rect(start, end).Normalize());
        }
        var playhead = X(model.DisplayFrame); context.DrawLine(new Pen(Brushes.OrangeRed, 1.5), new Point(playhead, 0), new Point(playhead, Bounds.Height));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); if (Model is not { } model || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus(); model.Cancel(); var point = e.GetPosition(this); _version = model.InteractionVersion; _origin = point;
        _extend = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta); _prior = model.Selection.ToArray();
        if (IsRuler) { _seeking = true; model.PreviewSeek(FrameAt(point.X)); }
        else
        {
            var index = (int)(point.Y / model.RowHeight); var row = index >= 0 && index < model.Rows.Count ? model.Rows[index] : null;
            var hit = row?.Keys.Where(key => Math.Abs(X(key.Frame) - point.X) <= 8).OrderBy(key => Math.Abs(X(key.Frame) - point.X)).FirstOrDefault();
            if (hit is not null && Math.Abs((index + .5) * model.RowHeight - point.Y) <= 10)
            {
                var ids = row!.Keys.Where(key => key.Frame == hit.Frame).Select(key => key.Id).ToArray();
                if (_extend || !ids.All(model.Selection.Contains)) model.Select(ids, toggle: _extend);
                _pressedIds = ids; _keyPress = true; _summary = row.IsSummary || !ids.Any(model.Selection.Contains);
            }
            else { if (!_extend) model.Select([]); _boxEnd = point; }
        }
        _pointer = e.Pointer; e.Pointer.Capture(this); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); if (_origin is not { } origin || Model is not { } model) return;
        var point = e.GetPosition(this);
        if (_seeking) model.PreviewSeek(FrameAt(point.X));
        else if (_keyPress && !_summary)
        {
            if (!_drag && Math.Abs(point.X - origin.X) >= 3) { _drag = model.StartMove(); _version = model.InteractionVersion; }
            if (_drag) model.UpdateMove((int)Math.Clamp(Math.Round((point.X - origin.X) / model.PixelsPerFrame, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue));
        }
        else if (!_keyPress)
        {
            _boxEnd = point; var area = new Rect(origin, point).Normalize();
            var ids = model.Rows.SelectMany((row, index) => row.IsSummary ? [] : row.Keys.Where(key => area.Contains(new Point(X(key.Frame), (index + .5) * model.RowHeight))).Select(key => key.Id));
            model.Select(_extend ? _prior.Concat(ids) : ids);
        }
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); if (_origin is null) return;
        var finish = _drag && Model?.InteractionVersion == _version;
        var seek = _seeking && Model?.InteractionVersion == _version ? FrameAt(e.GetPosition(this).X) : (int?)null;
        var clickSelection = _keyPress && !_drag && !_extend ? _pressedIds : null;
        ReleaseLocal(); e.Pointer.Capture(null);
        if (seek is { } frame) Model?.Seek(frame);
        if (clickSelection is not null) Model?.Select(clickSelection);
        if (finish) Model?.FinishMove(); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { if (_drag || _seeking) Model?.Cancel(); ReleaseLocal(); base.OnPointerCaptureLost(e); }
    private void ReleaseLocal()
    {
        _origin = null; _boxEnd = null; _drag = false; _keyPress = false; _summary = false; _seeking = false; _pressedIds = [];
        var pointer = _pointer; _pointer = null; pointer?.Capture(null); InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Model is not { } model) return;
        if (e.Key == Key.Escape) { if (model.HasPendingMove || _seeking) model.Cancel(); else model.Select([]); ReleaseLocal(); }
        else if (e.Key == Key.Delete) model.DeleteRequested?.Invoke();
        else if (e.Key == Key.Insert) model.AddRequested?.Invoke();
        else if (e.Key == Key.A && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))) model.Select(model.Rows.SelectMany(row => row.Keys.Select(key => key.Id)));
        else if (e.Key is Key.Left or Key.Right) model.Seek((int)Math.Clamp((long)model.Frame + (e.Key == Key.Left ? -1 : 1) * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1), 0, model.Duration));
        else if (e.Key == Key.Home) model.Seek(0);
        else if (e.Key == Key.End) model.Seek(model.Duration);
        else { base.OnKeyDown(e); return; }
        e.Handled = true;
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (Model is not { } model) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) { var x = e.GetPosition(this).X; var anchor = model.StartFrame + x / model.PixelsPerFrame; model.PixelsPerFrame *= Math.Pow(1.2, e.Delta.Y); model.StartFrame = anchor - x / model.PixelsPerFrame; }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.Delta.X != 0) model.StartFrame -= (e.Delta.X != 0 ? e.Delta.X : e.Delta.Y) * 60 / model.PixelsPerFrame;
        else { base.OnPointerWheelChanged(e); return; }
        e.Handled = true;
    }
}
