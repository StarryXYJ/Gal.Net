using GalNet.Core.Variable;
using GalNet.Editor.ViewModels;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GeneralTest.Editor;

public class VariableListEditorViewModelTests
{
    [Test]
    public void UpdateCurrentValue_RefreshesMatchingItem_WithoutWritingBack()
    {
        var defaultValue = new GalVariable { Name = "score" };
        defaultValue.SetValue(0);
        var definitions = new List<ProjectVariableDefinition>
        {
            new() { Name = "score", DefaultValue = defaultValue }
        };
        var writeCount = 0;
        var viewModel = new VariableListEditorViewModel(
            definitions,
            VariableScope.Player,
            showCurrentValue: true,
            allowCurrentEditing: true,
            (_, _) => true,
            (name, _) => name,
            _ => defaultValue,
            (_, _) => writeCount++,
            _ => { },
            (_, _) => { },
            () => { });
        var changedProperties = new List<string?>();
        viewModel.Items[0].PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        var runtimeValue = new GalVariable { Name = "score" };
        runtimeValue.SetValue(42);

        viewModel.UpdateCurrentValue("score", runtimeValue);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Items[0].CurrentIntValue, Is.EqualTo(42));
            Assert.That(changedProperties, Does.Contain(nameof(VariableEditorItemViewModel.CurrentIntValue)));
            Assert.That(writeCount, Is.Zero);
        });
    }
}
