using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

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

    [Test]
    public void GeneratedGalleryVariableNamesAreReservedForBuiltInState()
    {
        var documentService = new EditorDocumentService();
        documentService.Load(new LoadedEditorProjectDocument
        {
            Document = new EditorGraphDocument()
        });
        var service = new VariableDefinitionService(documentService);
        var definition = service.AddDefinition(VariableScope.Player);

        Assert.Multiple(() =>
        {
            Assert.That(service.IsNameAvailable("gallery_100_unlocked", VariableScope.Player), Is.False);
            Assert.That(
                service.RenameDefinition(VariableScope.Player, definition, "gallery_100_unlocked"),
                Is.False);
        });
    }
}
