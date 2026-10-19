using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using GalNet.Core.Scene;

namespace GalNet.Game.Controls.Timeline;

public sealed class TimelineKeyContextEventArgs(Guid key, Point position) : EventArgs
{
    public Guid Key { get; } = key;
    public Point Position { get; } = position;
}

public sealed partial class TimelineCanvas
{
    public event EventHandler<TimelineKeyContextEventArgs>? KeyContextRequested;
    private bool _curve, _curvePan, _handle, _incomingHandle;
    private int _curveRow;
    private TimelineCanvasKey? _curveKey;
    private Point _lastCurvePoint;
    private TimelineCanvasKey PreviewKey(TimelineCanvasKey key) => Model?.PreviewKeys.TryGetValue(key.Id, out var preview) == true ? preview : key;
    private Point CurvePoint(TimelineCanvasKey key, int row) => new(X(key.Frame), Model!.RowTop(row) + Model.RowHeight + Model.Rows[row].Curve!.Y(key.Value));
    private Point HandlePoint(TimelineCanvasKey key, int row, bool incoming)
    {
        var viewport = Model!.Rows[row].Curve!; var tangent = incoming ? key.InTangent : key.OutTangent;
        var slope = -(double)tangent * (TimelineCurveViewport.Height - 2 * TimelineCurveViewport.Padding) / viewport.Range / Model.PixelsPerFrame;
        var dx = 44 / Math.Sqrt(1 + slope * slope) * (incoming ? -1 : 1);
        return CurvePoint(key, row) + new Vector(dx, dx * slope);
    }
    private void RenderCurve(DrawingContext context, TimelineCanvasRow row, double top)
    {
        var index = Model!.Rows.ToList().IndexOf(row); var viewport = row.Curve!;
        using var clip = context.PushClip(new Rect(0, top, Bounds.Width, TimelineCurveViewport.Height));
        var keys = row.Keys.Select(PreviewKey).OrderBy(key => key.Frame).ToArray(); if (keys.Length == 0) return;
        var pen = new Pen(Model.HasPendingCurve && !Model.IsMoveValid ? Brushes.IndianRed : Brushes.MediumPurple, 1.5);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(12, 90, 90, 90)), null, new Rect(0, top, Bounds.Width, TimelineCurveViewport.Height));
        if (viewport.Minimum <= 0 && viewport.Maximum >= 0) context.DrawLine(new Pen(Brushes.Gray, .6), new Point(0, top + viewport.Y(0)), new Point(Bounds.Width, top + viewport.Y(0)));
        var track = TimelineCurveViewport.Track(keys);
        Point? previous = null;
        // Include exact key times so stepped segments have a vertical edge at their destination.
        var xs = Enumerable.Range(0, (int)Math.Ceiling(Bounds.Width / 2) + 1).Select(pixel => pixel * 2d)
            .Concat(keys.Select(key => X(key.Frame))).Where(x => x >= 0 && x <= Bounds.Width).Distinct().Order().ToArray();
        foreach (var x in xs)
        {
            var frame = Model.StartFrame + x / Model.PixelsPerFrame; var value = TimelineSampler.Evaluate(track, frame);
            if (!float.IsFinite(value)) { previous = null; continue; }
            var point = new Point(x, top + viewport.Y(value));
            if (previous is { } start)
            {
                var segment = keys.LastOrDefault(key => key.Frame < frame);
                if (segment?.Interpolation == AnimationInterpolation.Step)
                { var corner = new Point(point.X, start.Y); context.DrawLine(pen, start, corner); context.DrawLine(pen, corner, point); }
                else context.DrawLine(pen, start, point);
            }
            previous = point;
        }
        foreach (var key in keys)
        {
            var position = CurvePoint(key, index); var selected = Model.Selection.Contains(key.Id);
            if (selected)
            {
                var ordinal = Array.IndexOf(keys, key);
                foreach (var incoming in new[] { true, false })
                {
                    if (incoming ? ordinal == 0 || keys[ordinal - 1].Interpolation != AnimationInterpolation.CubicHermite : ordinal == keys.Length - 1 || key.Interpolation != AnimationInterpolation.CubicHermite) continue;
                    var handle = HandlePoint(key, index, incoming); context.DrawLine(new Pen(Brushes.DodgerBlue, 1), position, handle);
                    context.DrawEllipse(Brushes.White, new Pen(Brushes.DodgerBlue, 1), handle, 4, 4);
                }
            }
            context.DrawRectangle(selected ? Brushes.DodgerBlue : Brushes.MediumPurple, new Pen(Brushes.White, 1), new Rect(position.X - 4, position.Y - 4, 8, 8));
        }
        void Label(double value, double y) => context.DrawText(new FormattedText(value.ToString("G5", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 10, Brushes.Gray), new Point(Math.Max(4, Bounds.Width - 80), y));
        Label(viewport.Maximum, top + 2); Label(viewport.Minimum, top + TimelineCurveViewport.Height - 15);
    }
    private static double Distance(Point left, Point right) => Math.Sqrt(Math.Pow(left.X - right.X, 2) + Math.Pow(left.Y - right.Y, 2));
    private (TimelineCanvasKey? Key, bool Handle, bool Incoming) HitCurve(Point point, int row)
    {
        var keys = Model!.Rows[row].Keys;
        foreach (var key in keys.Where(key => Model.Selection.Contains(key.Id)))
        {
            var index = keys.ToList().IndexOf(key);
            if (index > 0 && keys[index - 1].Interpolation == AnimationInterpolation.CubicHermite && Distance(HandlePoint(key, row, true), point) <= 7) return (key, true, true);
            if (index + 1 < keys.Count && key.Interpolation == AnimationInterpolation.CubicHermite && Distance(HandlePoint(key, row, false), point) <= 7) return (key, true, false);
        }
        return (keys.OrderBy(key => Distance(CurvePoint(key, row), point)).FirstOrDefault(key => Distance(CurvePoint(key, row), point) <= 9), false, false);
    }
    private void PressCurve(PointerPressedEventArgs e, int row, Point point)
    {
        _curveRow = row; _lastCurvePoint = point;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) _curvePan = true;
        else
        {
            var hit = HitCurve(point, row);
            if (hit.Key is not { } key) { Model!.Select([]); _origin = null; e.Handled = true; return; }
            if (_extend) Model!.Select([key.Id], toggle: true);
            else if (!Model!.Selection.Contains(key.Id)) Model.Select([key.Id]);
            if (!Model!.Selection.Contains(key.Id)) { _origin = null; e.Handled = true; return; }
            _curve = true; _curveKey = key; _handle = hit.Handle; _incomingHandle = hit.Incoming;
        }
        _pointer = e.Pointer; e.Pointer.Capture(this); e.Handled = true;
    }
    private void MoveCurve(Point point)
    {
        var viewport = Model!.Rows[_curveRow].Curve!;
        if (_curvePan)
        { viewport.Pan((point.Y - _lastCurvePoint.Y) * viewport.Range / (TimelineCurveViewport.Height - 2 * TimelineCurveViewport.Padding)); _lastCurvePoint = point; Model.Changed(); return; }
        if (_origin is not { } origin || _curveKey is null) return;
        if (!_drag && Distance(point, origin) >= 3) { _drag = Model.StartCurve(); _version = Model.InteractionVersion; }
        if (!_drag) return;
        TimelineCurveGesture gesture;
        if (_handle)
        {
            var dx = (point.X - X(_curveKey.Frame)) / Model.PixelsPerFrame;
            var value = viewport.ValueAt(point.Y - Model.RowTop(_curveRow) - Model.RowHeight);
            var tangent = Math.Abs(dx) < .01 || (_incomingHandle ? dx >= 0 : dx <= 0) ? float.NaN : (float)((value - _curveKey.Value) / dx);
            gesture = new(Tangent: tangent, Incoming: _incomingHandle);
        }
        else gesture = new((int)Math.Clamp(Math.Round((point.X - origin.X) / Model.PixelsPerFrame, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue),
            (float)((origin.Y - point.Y) * viewport.Range / (TimelineCurveViewport.Height - 2 * TimelineCurveViewport.Padding)));
        Model.UpdateCurve(gesture);
    }
    private void RequestKeyContext(PointerPressedEventArgs e)
    {
        if (IsRuler || Model is not { } model) return;
        Focus(); model.Cancel(); var point = e.GetPosition(this); var row = model.RowAt(point.Y); if (row < 0) return;
        TimelineCanvasKey? key;
        if (model.Rows[row].Curve is not null && point.Y >= model.RowTop(row) + model.RowHeight) key = HitCurve(point, row).Key;
        else key = model.Rows[row].Keys.FirstOrDefault(key => Math.Abs(X(key.Frame) - point.X) <= 8 && Math.Abs(model.RowTop(row) + model.RowHeight / 2 - point.Y) <= 10);
        if (key is null) return;
        if (!model.Selection.Contains(key.Id)) model.Select(model.Rows[row].Keys.Where(item => item.Frame == key.Frame && (model.Rows[row].IsSummary || item.Id == key.Id)).Select(item => item.Id));
        KeyContextRequested?.Invoke(this, new(key.Id, point)); e.Handled = true;
    }
}
