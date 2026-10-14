using GalNet.Core.Entry;

namespace GalNet.Contracts.Builtins;

public sealed class PlayAudioEntry : PrimitiveEntry
{
    public const string TypeId = "audio.play";
    public override string Type => TypeId;
}

public sealed class StopAudioEntry : PrimitiveEntry
{
    public const string TypeId = "audio.stop";
    public override string Type => TypeId;
}

public sealed class PauseAudioEntry : PrimitiveEntry { public const string TypeId = "audio.pause"; public override string Type => TypeId; }
public sealed class ResumeAudioEntry : PrimitiveEntry { public const string TypeId = "audio.resume"; public override string Type => TypeId; }
public sealed class NextAudioEntry : PrimitiveEntry { public const string TypeId = "audio.next"; public override string Type => TypeId; }
public sealed class ClearAudioEntry : PrimitiveEntry { public const string TypeId = "audio.clear"; public override string Type => TypeId; }

public sealed class EnqueueAudioEntry : PrimitiveEntry
{
    public const string TypeId = "audio.enqueue";
    public override string Type => TypeId;
}

public sealed class ApplyAudioSEffectEntry : PrimitiveEntry
{
    public const string TypeId = "audio.seffect.apply";
    public override string Type => TypeId;
}

public sealed class StopAudioSEffectEntry : PrimitiveEntry
{
    public const string TypeId = "audio.seffect.stop";
    public override string Type => TypeId;
}

public sealed class PlayVideoEntry : PrimitiveEntry
{
    public const string TypeId = "video.play";
    public override string Type => TypeId;
}

public sealed class StopVideoEntry : PrimitiveEntry
{
    public const string TypeId = "video.stop";
    public override string Type => TypeId;
}
