using System.ComponentModel;
using GalNet.Core.Scene;

namespace GalNet.Game.Controls.Timeline;

public sealed record TimelineCanvasKey(Guid Id, int Frame, float Value = 0, float InTangent = 0, float OutTangent = 0, AnimationInterpolation Interpolation = AnimationInterpolation.Linear);
public sealed record TimelineCanvasRow(IReadOnlyList<TimelineCanvasKey> Keys, bool IsSummary, TimelineCurveViewport? Curve = null)
{
    public double Height => 38 + (Curve is null ? 0 : TimelineCurveViewport.Height);
}

/// <summary>Host-neutral view state. Author writes are delegated to the owning host.</summary>
public sealed class TimelineCanvasModel : INotifyPropertyChanged
{
    private double _start, _scale = 8;
    private int _frame, _duration = 60;
    private int? _seekPreview;
    private IReadOnlyList<TimelineCanvasRow> _rows = [];
    private readonly HashSet<Guid> _selection = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<int>? SeekRequested;
    public Func<bool>? BeginMove { get; set; }
    public Func<int, bool>? PreviewMove { get; set; }
    public Func<int, bool>? CommitMove { get; set; }
    public Func<bool>? BeginCurve { get; set; }
    public Func<TimelineCurveGesture, bool>? PreviewCurve { get; set; }
    public Func<TimelineCurveGesture, bool>? CommitCurve { get; set; }
    public bool HasPendingCurve { get; private set; }
    public TimelineCurveGesture? CurveGesture { get; private set; }
    public IReadOnlyDictionary<Guid, TimelineCanvasKey> PreviewKeys { get; set; } = new Dictionary<Guid, TimelineCanvasKey>();
    public Action? CancelMove { get; set; }
    public Action? DeleteRequested { get; set; }
    public Action? AddRequested { get; set; }
    public IReadOnlySet<Guid> Selection => _selection;
    public int InteractionVersion { get; private set; }
    public int DisplayFrame => _seekPreview ?? Frame;
    public int PreviewDelta { get; private set; }
    public bool HasPendingMove { get; private set; }
    public bool IsMoveValid { get; private set; } = true;
    public double RowHeight => 38;
    public double ContentHeight => Math.Max(RowHeight, Rows.Sum(row => row.Height));
    public IReadOnlyList<TimelineCanvasRow> Rows { get => _rows; set { _rows = value; Changed(); Changed(nameof(ContentHeight)); } }
    public double RowTop(int index) => Rows.Take(index).Sum(row => row.Height);
    public int RowAt(double y)
    { var top = 0d; for (var index = 0; index < Rows.Count; index++) { top += Rows[index].Height; if (y < top) return index; } return -1; }
    public int Duration { get => _duration; set { _duration = Math.Max(0, value); _frame = Math.Min(_frame, _duration); _start = Math.Min(_start, _duration); Changed(); Changed(nameof(StartFrame)); Changed(nameof(Frame)); } }
    public int Frame { get => _frame; set { var next = Math.Clamp(value, 0, Duration); if (_frame == next) return; _frame = next; Changed(); } }
    public double StartFrame { get => _start; set { if (!double.IsFinite(value)) return; _start = Math.Clamp(value, 0, Duration); Changed(); } }
    public double PixelsPerFrame { get => _scale; set { if (!double.IsFinite(value)) return; _scale = Math.Clamp(value, .5, 64); Changed(); } }
    public void PreviewSeek(int frame) { _seekPreview = Math.Clamp(frame, 0, Duration); Changed(nameof(DisplayFrame)); }
    public void Seek(int frame) { _seekPreview = null; Changed(nameof(DisplayFrame)); SeekRequested?.Invoke(Math.Clamp(frame, 0, Duration)); }
    public void Select(IEnumerable<Guid> ids, bool extend = false, bool toggle = false)
    {
        var selected = ids.Distinct().ToArray();
        if (!extend && !toggle) _selection.Clear();
        foreach (var id in selected) if (!toggle || !_selection.Remove(id)) _selection.Add(id);
        Changed(nameof(Selection));
    }
    public bool StartMove() { if (BeginMove?.Invoke() != true) return false; HasPendingMove = true; Changed(null); return true; }
    public void UpdateMove(int delta)
    {
        if (!HasPendingMove) return;
        PreviewDelta = delta; IsMoveValid = PreviewMove?.Invoke(delta) == true; Changed(null);
    }
    public bool FinishMove()
    {
        if (!HasPendingMove) return false;
        var delta = PreviewDelta;
        if (CommitMove?.Invoke(delta) != true) { IsMoveValid = false; Changed(null); return false; }
        ResetPreview(); return true;
    }
    public bool StartCurve() { if (BeginCurve?.Invoke() != true) return false; HasPendingCurve = true; Changed(null); return true; }
    public void UpdateCurve(TimelineCurveGesture gesture)
    { if (!HasPendingCurve) return; CurveGesture = gesture; IsMoveValid = PreviewCurve?.Invoke(gesture) == true; Changed(null); }
    public void FinishCurve()
    { if (HasPendingCurve && CurveGesture is { } gesture && IsMoveValid) CommitCurve?.Invoke(gesture); Cancel(); }
    public void Cancel() { CancelMove?.Invoke(); ResetPreview(); }
    public void ResetPreview() { InteractionVersion++; HasPendingCurve = false; CurveGesture = null; PreviewKeys = new Dictionary<Guid, TimelineCanvasKey>(); _seekPreview = null; PreviewDelta = 0; HasPendingMove = false; IsMoveValid = true; Changed(null); }
    public void Invalidate() { ResetPreview(); Select([]); }
    public void Changed([System.Runtime.CompilerServices.CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
