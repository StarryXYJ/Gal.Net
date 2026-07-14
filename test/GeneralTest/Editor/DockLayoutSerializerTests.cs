using Dock.Model.Mvvm.Controls;
using GalNet.Editor.Dock;

namespace GeneralTest.Editor;

public class DockLayoutSerializerTests
{
    [Test]
    public void Serialize_AllowsNonFiniteDockValues()
    {
        var layout = new RootDock { Proportion = double.NaN };

        var serialized = new DockLayoutSerializer().Serialize(layout);

        Assert.That(serialized, Does.Contain("\"NaN\""));
        Assert.That(new DockLayoutSerializer().Deserialize(serialized), Is.Not.Null);
    }
}
