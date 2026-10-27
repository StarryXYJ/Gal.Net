using GalNet.Contracts.Builtins.Audio;

namespace GeneralTest;

public class AudioConfigurationTests
{
    [Test]
    public void EmptyUngroupedAndCustomDeclarationsAreValidOwnedValues()
    {
        Assert.That(AudioConfiguration.Empty.Tracks, Is.Empty);
        var groups = new List<AudioBusDefinition> { new() { Id = "ambience" } };
        var tracks = new List<AudioTrackDefinition>
        {
            new() { Id = "rain", BusId = "ambience", PreserveDuringCatchUp = true },
            new() { Id = "alerts" }
        };
        var configuration = new AudioConfiguration(tracks, groups);
        tracks[0] = tracks[0] with { BusId = "other" };
        groups.Clear();
        Assert.That(configuration.Buses.Single().Id, Is.EqualTo("ambience"));
        Assert.That(configuration.TryGetTrack("rain", out var rain), Is.True);
        Assert.That(rain.BusId, Is.EqualTo("ambience"));
        Assert.That(rain.PreserveDuringCatchUp, Is.True);
        Assert.That(configuration.Tracks[1].BusId, Is.Null);
    }

    [Test]
    public void ConfigurationRejectsBrokenDeclarationsAtTheBoundary()
    {
        Assert.Throws<InvalidDataException>(() => new AudioConfiguration(buses: [new() { Id = "x" }, new() { Id = "x" }]));
        Assert.Throws<InvalidDataException>(() => new AudioConfiguration([new() { Id = "x" }, new() { Id = "x" }]));
        Assert.Throws<InvalidDataException>(() => new AudioConfiguration([new() { Id = "x", BusId = "missing" }]));
        Assert.Throws<InvalidDataException>(() => new AudioConfiguration([new() { Id = "" }]));
        Assert.Throws<InvalidDataException>(() => new AudioConfiguration([new() { Id = "x", DefaultGain = float.NaN }]));
        Assert.Throws<InvalidDataException>(() => new AudioConfiguration([new() { Id = "x", MaxVoices = 0 }]));
        Assert.Throws<InvalidDataException>(() => new AudioConfiguration([new() { Id = "x", MaxQueuedItems = -1 }]));
        Assert.Throws<InvalidDataException>(() => new AudioConfiguration([new() { Id = "x", OverflowPolicy = (AudioOverflowPolicy)99 }]));
    }
}
