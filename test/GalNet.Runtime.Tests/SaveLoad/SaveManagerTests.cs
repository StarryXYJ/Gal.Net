using GalNet.Core.Runtime;
using GalNet.Runtime.SaveLoad;

namespace GeneralTest.Runtime;

public class SaveManagerTests
{
    [Test]
    public void Serialize_WritesTheCurrentSnapshotVersion()
    {
        var snapshot = new GameSnapshot { NodeId = "intro" };

        var json = SaveManager.Serialize(snapshot);
        var restored = SaveManager.Deserialize(json);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Version, Is.EqualTo(GameSnapshot.CurrentFormatVersion));
    }

    [Test]
    public void Deserialize_RejectsPreviousSnapshotVersions()
    {
        Assert.That(SaveManager.Deserialize("""{ "version": 1, "nodeId": "intro" }"""), Is.Null);
        Assert.That(SaveManager.Deserialize("""{ "nodeId": "intro" }"""), Is.Null);
    }
}
