using System.Collections.ObjectModel;
using GalNet.Editor.History;

namespace GeneralTest.Editor;

[TestFixture]
public sealed class UndoRedoHistoryTests
{
    [Test]
    public void ExecuteUndoRedo_PreservesObjectIdentity()
    {
        var model = new Model { Name = "before" };
        var history = new UndoRedoHistory();
        history.Execute(new PropertyEdit<string>("Rename", value => model.Name = value, model.Name, "after"));

        Assert.That(model.Name, Is.EqualTo("after"));
        history.Undo();
        Assert.That(model.Name, Is.EqualTo("before"));
        history.Redo();
        Assert.That(model.Name, Is.EqualTo("after"));
    }

    [Test]
    public void NewEditAfterUndo_ClearsRedo()
    {
        var value = 0;
        var history = new UndoRedoHistory();
        history.Execute(new PropertyEdit<int>("One", next => value = next, 0, 1));
        history.Undo();
        history.Execute(new PropertyEdit<int>("Two", next => value = next, 0, 2));

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(2));
            Assert.That(history.CanRedo, Is.False);
        });
    }

    [Test]
    public void SavedCheckpointTracksUndoAndRedo()
    {
        var value = 0;
        var history = new UndoRedoHistory();
        history.Execute(new PropertyEdit<int>("One", next => value = next, 0, 1));
        history.MarkSaved();
        history.Execute(new PropertyEdit<int>("Two", next => value = next, 1, 2));
        Assert.That(history.IsDirty, Is.True);
        history.Undo();
        Assert.That(history.IsDirty, Is.False);
        history.Redo();
        Assert.That(history.IsDirty, Is.True);
    }

    [Test]
    public void CompositeEditUndoesInReverseOrder()
    {
        var items = new ObservableCollection<string>();
        var history = new UndoRedoHistory();
        history.Execute(new CompositeEdit("Add pair",
        [
            new CollectionInsertEdit<string>("A", items, "A", 0),
            new CollectionInsertEdit<string>("B", items, "B", 1)
        ]));
        history.Undo();
        Assert.That(items, Is.Empty);
        history.Redo();
        Assert.That(items, Is.EqualTo(new[] { "A", "B" }));
    }

    [Test]
    public void RouterUsesActiveDomainHistory()
    {
        var graph = new UndoRedoHistory();
        var ui = new UndoRedoHistory();
        var graphValue = 0;
        var uiValue = 0;
        graph.Execute(new PropertyEdit<int>("Graph", next => graphValue = next, 0, 1));
        ui.Execute(new PropertyEdit<int>("UI", next => uiValue = next, 0, 1));
        var router = new UndoRedoRouter();

        router.SetActive(new Target(graph));
        router.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(graphValue, Is.Zero);
            Assert.That(uiValue, Is.EqualTo(1));
        });
    }

    private sealed class Model { public string Name { get; set; } = ""; }
    private sealed record Target(IUndoRedoHistory History) : IUndoRedoTarget
    {
        public IUndoRedoHistory? UndoRedoHistory => History;
    }
}
