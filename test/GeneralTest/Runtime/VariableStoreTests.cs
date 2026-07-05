using GalNet.Runtime.Variables;

namespace GeneralTest.Runtime;

public class VariableStoreTests
{
    [Test]
    public void Set_And_Get_Should_Work()
    {
        var store = new VariableStore();
        store.Set("player.score", 42);
        Assert.That(store.GetInt("player.score"), Is.EqualTo(42));
    }

    [Test]
    public void GetBool_Should_Default_False()
    {
        var store = new VariableStore();
        Assert.That(store.GetBool("unknown"), Is.False);
    }

    [Test]
    public void GetInt_Should_Default_Zero()
    {
        var store = new VariableStore();
        Assert.That(store.GetInt("unknown"), Is.EqualTo(0));
    }

    [Test]
    public void GetFloat_Should_Default_Zero()
    {
        var store = new VariableStore();
        Assert.That(store.GetFloat("unknown"), Is.EqualTo(0f));
    }

    [Test]
    public void GetString_Should_Default_Empty()
    {
        var store = new VariableStore();
        Assert.That(store.GetString("unknown"), Is.EqualTo(""));
    }

    [Test]
    public void String_Value_Should_Work()
    {
        var store = new VariableStore();
        store.Set("player.name", "Alice");
        Assert.That(store.GetString("player.name"), Is.EqualTo("Alice"));
    }

    [Test]
    public void Bool_Value_Should_Work()
    {
        var store = new VariableStore();
        store.Set("player.flag", true);
        Assert.That(store.GetBool("player.flag"), Is.True);
    }

    [Test]
    public void RestoreFrom_Should_Replace_All()
    {
        var store = new VariableStore();
        store.Set("player.score", 100);
        store.Set("player.hp", 50);

        var snapshot = new Dictionary<GalNet.Core.Variable.VariableRoute, GalNet.Core.Variable.Variable>();
        var v = new GalNet.Core.Variable.Variable { Name = "player.restored" };
        v.SetValue(999);
        snapshot[new GalNet.Core.Variable.VariableRoute("player.restored")] = v;

        store.RestoreFrom(snapshot);

        // Old values gone
        Assert.That(store.GetInt("player.score"), Is.EqualTo(0));
        // New value present
        Assert.That(store.GetInt("player.restored"), Is.EqualTo(999));
    }
}
