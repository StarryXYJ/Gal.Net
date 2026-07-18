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
    public void Set_Same_Value_Should_Not_Notify_Again()
    {
        var notificationCount = 0;
        var store = new VariableStore(onVariableChanged: (_, _, _) => notificationCount++);

        store.Set("player.score", 42);
        store.Set("player.score", 42);

        Assert.That(notificationCount, Is.EqualTo(1));
    }

    [Test]
    public void Set_Feedback_With_Same_Value_Should_Not_Recurse()
    {
        var notificationCount = 0;
        VariableStore? store = null;
        store = new VariableStore(_ => GalNet.Core.Variable.VariableScope.Player, (_, name, variable) =>
        {
            notificationCount++;
            store!.Set(name, variable.AsInt());
        });

        store.Set("score", 42);

        Assert.That(notificationCount, Is.EqualTo(1));
        Assert.That(store.GetInt("player.score"), Is.EqualTo(42));
    }

    [Test]
    public void RestoreSaveFrom_Should_Only_Replace_Save_Variables()
    {
        var store = new VariableStore();
        store.Set("player.score", 100);
        store.Set("save.hp", 50);

        var snapshot = new Dictionary<string, GalNet.Core.Variable.Variable>();
        var v = new GalNet.Core.Variable.Variable { Name = "restored" };
        v.SetValue(999);
        snapshot["restored"] = v;

        store.RestoreSaveFrom(snapshot);

        Assert.That(store.GetInt("player.score"), Is.EqualTo(100));
        Assert.That(store.GetInt("save.hp"), Is.EqualTo(0));
        Assert.That(store.GetInt("restored"), Is.EqualTo(999));
    }
}
