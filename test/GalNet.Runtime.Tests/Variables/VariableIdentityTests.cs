using GalNet.Core.Variable;
using GalNet.Runtime.Variables;

namespace GeneralTest.Runtime;

public sealed class VariableIdentityTests
{
    [Test]
    public void RestoreAndNotifyPreserveIdentityWithoutSharingMutableVariable()
    {
        var source = new Variable { Uid = "score-id", Name = "score" };
        source.SetValue(42);
        Variable? notification = null;
        var store = new VariableStore(onVariableChanged: (_, _, value) => notification = value);
        store.RestoreSaveFrom(new Dictionary<string, Variable> { ["score"] = source });
        source.SetValue(99);
        store.Set("score", 43);

        Assert.Multiple(() =>
        {
            Assert.That(store.GetInt("score"), Is.EqualTo(43));
            Assert.That(notification!.Uid, Is.EqualTo("score-id"));
            Assert.That(notification, Is.Not.SameAs(source));
            Assert.That(source.AsInt(), Is.EqualTo(99));
        });
    }
}
