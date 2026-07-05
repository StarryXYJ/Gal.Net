using GalNet.Runtime.Audio;

namespace GeneralTest.Runtime.Audio;

public class AudioChannelManagerTests
{
    [Test]
    public void Initialize_Should_Create_Default_Channels()
    {
        var mgr = new AudioChannelManager();
        mgr.Initialize();

        Assert.That(mgr.GetChannel("bgm"), Is.Not.Null);
        Assert.That(mgr.GetChannel("bgm2"), Is.Not.Null);
        Assert.That(mgr.GetChannel("voice"), Is.Not.Null);
        Assert.That(mgr.GetChannel("sfx1"), Is.Not.Null);
        Assert.That(mgr.GetChannel("sfx2"), Is.Not.Null);
    }

    [Test]
    public void Play_Should_Set_Channel_State()
    {
        var mgr = new AudioChannelManager();
        mgr.Initialize();

        mgr.Play("bgm", "bgm_01", 0.8f, "once", 1);

        var ch = mgr.GetChannel("bgm");
        Assert.That(ch!.State, Is.EqualTo(AudioState.Playing));
        Assert.That(ch.CurrentAsset, Is.EqualTo("bgm_01"));
        Assert.That(ch.Volume, Is.EqualTo(0.8f));
    }

    [Test]
    public void Stop_Should_Clear_Channel()
    {
        var mgr = new AudioChannelManager();
        mgr.Initialize();
        mgr.Play("voice", "voice_01", 1f, "once", 1);
        mgr.Stop("voice");

        var ch = mgr.GetChannel("voice");
        Assert.That(ch!.State, Is.EqualTo(AudioState.Stopped));
        Assert.That(ch.CurrentAsset, Is.Null);
    }

    [Test]
    public void Pause_Resume_Should_Work()
    {
        var mgr = new AudioChannelManager();
        mgr.Initialize();
        mgr.Play("sfx1", "sfx_bang", 1f, "once", 1);

        mgr.Pause("sfx1");
        Assert.That(mgr.GetChannel("sfx1")!.State, Is.EqualTo(AudioState.Paused));

        mgr.Resume("sfx1");
        Assert.That(mgr.GetChannel("sfx1")!.State, Is.EqualTo(AudioState.Playing));
    }

    [Test]
    public void StopAll_Should_Stop_All()
    {
        var mgr = new AudioChannelManager();
        mgr.Initialize();
        mgr.Play("bgm", "bgm_01", 1f, "repeat", -1);
        mgr.Play("voice", "voice_01", 1f, "once", 1);

        mgr.StopAll();

        Assert.That(mgr.AllChannels.All(c => c.State == AudioState.Stopped), Is.True);
    }
}
