using GalNet.Core.Graph;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;

namespace GeneralTest.Editor;

[TestFixture]
public class GraphChangeTrackerTests
{
    [Test]
    public void Track_MarksPersistedChanges_AndClearStopsTracking()
    {
        var changes = 0;
        using var tracker = new GraphChangeTracker(() => changes++, () => false);
        var node = new GraphNode(new Group { Name = "Start" }, GraphNodeKind.LinearGroup);
        tracker.Track(node);

        node.Name = "Renamed";
        node.Entries[0].Parameters = new Dictionary<string, string> { ["content"] = "Hello" };

        Assert.That(changes, Is.EqualTo(2));

        tracker.Clear();
        node.Name = "Ignored";

        Assert.That(changes, Is.EqualTo(2));
    }
}
