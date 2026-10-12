using GalNet.Core.Variable;
using GalNetVar = GalNet.Core.Variable.Variable;

namespace GeneralTest.Variable;

public class VariableTests
{
    [TestCase(true)]
    [TestCase(42)]
    [TestCase(0.75f)]
    [TestCase("Alice")]
    public void CurrentJsonFormatRoundTripsTypedValues(object value)
    {
        var variable = new GalNetVar { Name = "test" };
        variable.SetValue(value);
        var restored = System.Text.Json.JsonSerializer.Deserialize<GalNetVar>(System.Text.Json.JsonSerializer.Serialize(variable))!;
        Assert.Multiple(() =>
        {
            Assert.That(restored.Uid, Is.EqualTo(variable.Uid));
            Assert.That(restored.Type, Is.EqualTo(variable.Type));
            Assert.That(restored.AsString(), Is.EqualTo(variable.AsString()));
        });
    }

    [Test]
    public void OldStringEncodedIntegerIsRejected()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<GalNetVar>(
            "{\"Uid\":\"x\",\"Name\":\"test\",\"Type\":1,\"Value\":\"42\"}"));
    }

    [Test]
    public void Variable_Should_Store_Bool()
    {
        var v = new GalNetVar { Name = "flag" };
        v.SetValue(true);
        Assert.That(v.AsBool(), Is.True);
        Assert.That(v.Type, Is.EqualTo(VariableType.Bool));
        Assert.That(v.AsString(), Is.EqualTo("true"));
    }

    [Test]
    public void Variable_Should_Store_Int()
    {
        var v = new GalNetVar { Name = "score" };
        v.SetValue(42);
        Assert.That(v.AsInt(), Is.EqualTo(42));
        Assert.That(v.AsFloat(), Is.EqualTo(42f));
        Assert.That(v.Type, Is.EqualTo(VariableType.Int));
    }

    [Test]
    public void Variable_Should_Store_Float()
    {
        var v = new GalNetVar { Name = "ratio" };
        v.SetValue(0.75f);
        Assert.That(v.AsFloat(), Is.EqualTo(0.75f));
        Assert.That(v.Type, Is.EqualTo(VariableType.Float));
    }

    [Test]
    public void Variable_Should_Store_String()
    {
        var v = new GalNetVar { Name = "name" };
        v.SetValue("Alice");
        Assert.That(v.AsString(), Is.EqualTo("Alice"));
        Assert.That(v.Type, Is.EqualTo(VariableType.String));
    }

    [Test]
    public void Variable_Uid_Should_Be_Unique()
    {
        var v1 = new GalNetVar();
        var v2 = new GalNetVar();
        Assert.That(v1.Uid, Is.Not.EqualTo(v2.Uid));
    }

    [Test]
    public void Variable_Default_Type_Should_Be_String()
    {
        var v = new GalNetVar();
        Assert.That(v.Type, Is.EqualTo(VariableType.String));
    }
}

public class VariableRouteTests
{
    private static readonly string[] PlayerAffectionAlice = ["player", "affection", "alice"];

    [Test]
    public void Route_Should_Parse_Segments()
    {
        var route = new VariableRoute("player.affection.alice");
        Assert.That(route.Path, Is.EqualTo("player.affection.alice"));
        Assert.That(route.Segments, Is.EquivalentTo(PlayerAffectionAlice));
    }

    [Test]
    public void Route_Should_Be_Equatable()
    {
        var r1 = new VariableRoute("player.score");
        var r2 = new VariableRoute("player.score");
        var r3 = new VariableRoute("player.level");

        Assert.That(r1, Is.EqualTo(r2));
        Assert.That(r1, Is.Not.EqualTo(r3));
        Assert.That(r1.Equals(r2), Is.True);
    }

    [Test]
    public void Route_Should_Support_Implicit_Conversion()
    {
        VariableRoute route = "player.hp";
        string path = route;

        Assert.That(path, Is.EqualTo("player.hp"));
    }
}
