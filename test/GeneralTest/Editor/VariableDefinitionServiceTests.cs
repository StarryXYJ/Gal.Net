using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Services;

namespace GeneralTest.Editor;

[TestFixture]
public class VariableDefinitionServiceTests
{
    [Test]
    public void AddDefinition_UsesUniqueNamesAndMarksDocumentDirty()
    {
        var documentService = new EditorDocumentService();
        documentService.Load(new LoadedEditorProjectDocument
        {
            Document = new EditorGraphDocument
            {
                PlayerVariables =
                [
                    new ProjectVariableDefinition
                    {
                        Name = "var_player_1",
                        DefaultValue = new GalNet.Core.Variable.Variable { Name = "var_player_1", Value = VariableValue.From(false) }
                    }
                ]
            }
        });

        var service = new VariableDefinitionService(documentService);

        var created = service.AddDefinition(VariableScope.Player);

        Assert.That(created.Name, Is.EqualTo("var_player_2"));
        Assert.That(documentService.IsDirty, Is.True);
        Assert.That(service.GetDefinitions(VariableScope.Player), Has.Count.EqualTo(2));
    }
}
