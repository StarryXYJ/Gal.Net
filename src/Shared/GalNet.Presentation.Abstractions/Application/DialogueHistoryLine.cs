namespace GalNet.Presentation.Abstractions.Application;

/// <summary>Serializable replay intent, independent of an original backend voice.</summary>
public sealed record DialogueVoice(string AssetId, string TrackId, float Gain = 1f);

/// <summary>One completed visit to a dialogue or NVL entry, in the language the player saw.</summary>
public sealed record DialogueHistoryLine(string GroupId, string EntryId, string Speaker, string Text,
    bool WasRead, DialogueVoice? Voice = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasVoice => Voice is not null;
}
