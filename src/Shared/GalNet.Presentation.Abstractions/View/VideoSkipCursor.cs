namespace GalNet.Presentation.Abstractions.View;

/// <summary>Tracks requested seeks as well as the delayed backend playback clock.</summary>
public sealed class VideoSkipCursor(IReadOnlyList<double> points)
{
    private double _lastTarget = -1;

    public double? Next(double positionSeconds, double? durationSeconds)
    {
        var position = Math.Max(positionSeconds, _lastTarget);
        foreach (var point in points)
        {
            if (point <= position) continue;
            if (durationSeconds is > 0 && point >= durationSeconds) return null;
            _lastTarget = point;
            return point;
        }
        return null;
    }
}
