using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Core.Variable;
using GalNet.Editor.Controls;
using GalNet.Editor.ViewModels;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GeneralTest.Editor;

public class VariableListEditorViewModelTests
{
    [Test]
    public void UpdateCurrentValueRefreshesMatchingItemWithoutWritingBack()
    {
        var definition = Definition(VariableType.Int);
        var writeCount = 0;
        var viewModel = new VariableListEditorViewModel([definition], true, _ => definition.DefaultValue, (_, _) => writeCount++);
        var changedProperties = new List<string?>();
        viewModel.Items[0].PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        var runtimeValue = new GalVariable { Name = "score", Value = VariableValue.From(42) };
        viewModel.UpdateCurrentValue("score", runtimeValue);
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Items[0].CurrentIntValue, Is.EqualTo(42));
            Assert.That(changedProperties, Does.Contain(nameof(VariableEditorItemViewModel.CurrentIntValue)));
            Assert.That(writeCount, Is.Zero);
            Assert.That(definition.DefaultValue.AsInt(), Is.Zero);
        });
    }

    [TestCase(VariableType.Bool), TestCase(VariableType.Int), TestCase(VariableType.Float), TestCase(VariableType.String)]
    public void EditingCurrentValueOnlyWritesRuntimeAndDisabledRowsRejectWrites(VariableType type)
    {
        var definition = Definition(type);
        var original = definition.DefaultValue.Value.Serialize();
        var runtime = definition.DefaultValue.Clone();
        var writes = new List<object>();
        var viewModel = new VariableListEditorViewModel([definition], true, _ => runtime, (name, value) =>
        {
            Assert.That(name, Is.EqualTo("score")); writes.Add(value);
        });
        void Change()
        {
            var item = viewModel.Items[0];
            switch (type)
            {
                case VariableType.Bool: item.CurrentBoolValue = true; break;
                case VariableType.Int: item.CurrentIntValue = 42; break;
                case VariableType.Float: item.CurrentFloatValue = 1.5m; break;
                default: item.CurrentStringValue = "current"; break;
            }
        }
        Change();
        viewModel.SetCurrentValueVisibility(false);
        Change();
        Assert.Multiple(() =>
        {
            Assert.That(writes, Has.Count.EqualTo(1));
            Assert.That(definition.DefaultValue.Value.Serialize(), Is.EqualTo(original));
            Assert.That(runtime.Value.Serialize(), Is.EqualTo(original));
            Assert.That(viewModel.Items[0].Name, Is.EqualTo("score"));
            Assert.That(viewModel.Items[0].SelectedType, Is.EqualTo(type));
        });
    }

    [Test]
    public async Task MountedPreviewOnlyExposesRuntimeValueInputs()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(() =>
        {
            Avalonia.Application.Current!.Styles.Add(new Semi.Avalonia.SemiTheme());
            var definition = Definition(VariableType.Int);
            var writes = new List<object>();
            var model = new VariableListEditorViewModel([definition], true, _ => null, (_, value) => writes.Add(value));
            var view = new VariableListEditorControl { DataContext = model };
            var window = new Window { Content = view, Width = 700, Height = 300 };
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            try
            {
                Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "score"), Is.True);
                Assert.That(view.GetVisualDescendants().OfType<ComboBox>(), Is.Empty);
                var input = view.GetVisualDescendants().OfType<NumericUpDown>().Single(control => control.IsVisible);
                input.Value = 12; Dispatcher.UIThread.RunJobs();
                Assert.That(writes.Last(), Is.EqualTo(12));
                Assert.That(definition.DefaultValue.AsInt(), Is.Zero);
                model.UpdateCurrentValue("score", new() { Value = VariableValue.From(20) });
                Dispatcher.UIThread.RunJobs();
                Assert.That(input.Value, Is.EqualTo(20));
                Assert.That(writes, Has.Count.EqualTo(1), "Runtime refresh must not echo into another write.");
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    private static ProjectVariableDefinition Definition(VariableType type) => new()
    {
        Name = "score",
        DefaultValue = new() { Name = "score", Value = type switch
        {
            VariableType.Bool => VariableValue.From(false), VariableType.Int => VariableValue.From(0),
            VariableType.Float => VariableValue.From(0f), _ => VariableValue.From("")
        } }
    };
}
