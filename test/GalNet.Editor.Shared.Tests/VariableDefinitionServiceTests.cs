using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

[TestFixture]
public class VariableDefinitionServiceTests
{
    [Test]
    public void ReturnedDeclarationsAreDetachedFromAuthorContent()
    {
        var documents = new EditorDocumentService();
        var source = new ProjectVariableDefinition { Name = "score", DefaultValue = new() { Name = "score", Value = VariableValue.From(3) } };
        documents.Load(new() { Document = new() { PlayerVariables = [source] } });
        var service = new VariableDefinitionService(documents);
        var snapshot = service.GetDefinitions(VariableScope.Player).Single();
        snapshot.Name = "changed";
        snapshot.DefaultValue.SetValue(99);
        Assert.Multiple(() =>
        {
            Assert.That(source.Name, Is.EqualTo("score"));
            Assert.That(source.DefaultValue.AsInt(), Is.EqualTo(3));
            Assert.That(snapshot.DefaultValue.Uid, Is.EqualTo(source.DefaultValue.Uid));
            Assert.That(service.GetDefinitions(VariableScope.Save), Is.Empty);
        });
    }

    [Test]
    public void DeclarationRefreshNotifiesBothScopesAndReadsCurrentDocument()
    {
        var documents = new EditorDocumentService();
        documents.Load(new LoadedEditorProjectDocument());
        var service = new VariableDefinitionService(documents);
        var changed = new List<VariableScope>();
        service.DefinitionsChanged += changed.Add;
        documents.CurrentDocument.SaveVariables.Add(new() { Name = "saved" });
        service.ResetFromDocument();
        Assert.That(changed, Is.EqualTo(new[] { VariableScope.Player, VariableScope.Save }));
        Assert.That(service.GetDefinitions(VariableScope.Save).Single().Name, Is.EqualTo("saved"));
        documents.Unload();
        Assert.That(service.GetDefinitions(VariableScope.Save), Is.Empty);
    }
}
