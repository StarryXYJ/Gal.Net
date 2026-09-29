using GalNet.Core.Variable;
using GalNet.Editor.Models;

namespace GeneralTest.Editor;

public class VariableDefinitionCollectionTests
{
    [Test]
    public void Move_Reorders_TheBackingDefinitions()
    {
        var first = new ProjectVariableDefinition { Name = "first" };
        var second = new ProjectVariableDefinition { Name = "second" };
        var definitions = new List<ProjectVariableDefinition> { first, second };
        var collection = new VariableDefinitionCollection(definitions);

        Assert.That(collection.Move(second, 0), Is.True);
        Assert.That(definitions.Select(x => x.Name), Is.EqualTo(new[] { "second", "first" }));
    }

    [Test]
    public void Add_Creates_A_NameConsistent_DefaultValue()
    {
        var collection = new VariableDefinitionCollection([]);

        var definition = collection.Add("score");

        Assert.That(definition.Name, Is.EqualTo("score"));
        Assert.That(definition.DefaultValue.Name, Is.EqualTo("score"));
    }
}
