using System.Text.Json;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Game.Controls.Timeline;

namespace GalNet.Editor.Services;

public sealed partial class TimelineEditSession
{
    private readonly Dictionary<string, TimelineCurveViewport> _curveViews = new(StringComparer.Ordinal);
    private TimelineCurveViewport CurveView(TimelineTreeNode node, IReadOnlyList<TimelineCanvasKey> keys)
    {
        if (!_curveViews.TryGetValue(node.Key, out var viewport))
        { viewport = new(); viewport.Fit(keys); _curveViews.Add(node.Key, viewport); }
        return viewport;
    }
    public void FitCurve(TimelineTreeNode node)
    {
        var index = VisibleNodes.IndexOf(node);
        if (index < 0 || Canvas.Rows[index].Curve is not { } viewport) return;
        Canvas.Cancel(); viewport.Fit(Canvas.Rows[index].Keys); Canvas.Changed();
    }
    private JsonElement CurvePatch(TimelineCurveGesture gesture)
    {
        var patch = _moveDocument!.ChangeCurve(_moveKeys!, gesture);
        var document = new TimelineDocument(patch);
        foreach (var location in _moveKeys!)
        {
            var value = document.Tracks[location.Track].Keys.Single(key => key.Frame == location.Frame + gesture.FrameDelta).Value;
            if (Rows.FirstOrDefault(row => row.TrackIndex == location.Track)?.Capability is { } capability
                && document.Tracks[location.Track].BlendMode != AnimationBlendMode.Additive && !capability.Accepts(value))
                throw new InvalidDataException("Timeline.ValueRange");
        }
        return patch;
    }
    private bool PreviewCurve(TimelineCurveGesture gesture)
    {
        if (!IsMoveCurrent) return false;
        try
        {
            var document = new TimelineDocument(CurvePatch(gesture));
            Canvas.PreviewKeys = _keyIds.ToDictionary(pair => pair.Value, pair =>
            {
                var frame = pair.Key.Frame + (_moveKeys!.Contains(pair.Key) ? gesture.FrameDelta : 0);
                var key = document.Tracks[pair.Key.Track].Keys.Single(key => key.Frame == frame);
                return CanvasKey(pair.Value, key);
            });
            Message = ""; return true;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException or InvalidOperationException)
        { Canvas.PreviewKeys = new Dictionary<Guid, TimelineCanvasKey>(); EditError(error); return false; }
    }
    private bool CommitCurve(TimelineCurveGesture gesture)
    {
        if (!IsMoveCurrent) return false;
        try
        {
            var locations = _moveKeys!; var patch = CurvePatch(gesture);
            if (_baseline is { } original && JsonElement.DeepEquals(original, patch)) return true;
            if (!Submit(patch)) return false;
            Canvas.Select(locations.Select(location => _keyIds[new(location.Track, location.Frame + gesture.FrameDelta)])); return true;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException or InvalidOperationException)
        { EditError(error); return false; }
    }
    private static TimelineCanvasKey CanvasKey(Guid id, TimelineKeyframeDefinition key) => new(id, key.Frame, key.Value, key.InTangent, key.OutTangent, key.InterpolationToNext);
    internal bool SetTangentMode(TimelineKeyEdit draft, bool? incoming, TimelineTangentMode mode, bool? linked = null)
    {
        if (!IsKeyEditCurrent(draft)) return false;
        try
        {
            var patch = _document!.SetTangentMode(draft.Locations, incoming, mode, linked);
            if (_baseline is { } original && JsonElement.DeepEquals(original, patch)) return true;
            var locations = draft.Locations;
            if (!Submit(patch)) return false;
            Canvas.Select(locations.Select(location => _keyIds[location])); return true;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException or InvalidOperationException)
        { EditError(error); return false; }
    }
}
