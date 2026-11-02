namespace GalNet.Contracts.Builtins.Audio;

/// <summary>Saved ducking rule. Its current envelope is transient and owned by the PCM mixer.</summary>
public sealed record AudioDuckingState(string Id, string SourceTrackId, string TargetId,
    SEffectScope Scope = SEffectScope.Bus, float Gain = .35f, float AttackMs = 80, float ReleaseMs = 400)
{
    public void Validate(AudioConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(Id) || !configuration.TryGetTrack(SourceTrackId, out var source) || !configuration.HasTarget(Scope, TargetId) ||
            Scope == SEffectScope.Track && SourceTrackId == TargetId || Scope == SEffectScope.Bus && source.BusId == TargetId ||
            !float.IsFinite(Gain) || Gain is < 0 or > 1 || !float.IsFinite(AttackMs) || AttackMs is < 1 or > 5000 ||
            !float.IsFinite(ReleaseMs) || ReleaseMs is < 1 or > 10000)
            throw new InvalidDataException("Invalid ducking rule or self-ducking route.");
    }
}
