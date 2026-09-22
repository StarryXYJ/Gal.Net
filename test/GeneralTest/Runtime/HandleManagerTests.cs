using GalNet.Core.Primitives;
using System.Diagnostics.CodeAnalysis;

namespace GeneralTest.Runtime;

public class HandleManagerTests
{
    [Test]
    public void AddAndLookupRetainTheExactHandle()
    {
        using var handles = new HandleManager();
        var handle = new TestHandle(Guid.NewGuid());

        handles.Add(handle);

        Assert.That(handles.TryGet(handle.Id, out var resolved), Is.True);
        Assert.That(resolved, Is.SameAs(handle));
        Assert.That(handles.TryGet<TestHandle>(handle.Id, out var typed), Is.True);
        Assert.That(typed, Is.SameAs(handle));
    }

    [Test]
    public void AddRejectsEmptyOrDuplicateIdentifiers()
    {
        using var handles = new HandleManager();
        var id = Guid.NewGuid();
        handles.Add(new TestHandle(id));

        Assert.That(() => handles.Add(new TestHandle(Guid.Empty)), Throws.ArgumentException);
        Assert.That(() => handles.Add(new TestHandle(id)), Throws.InvalidOperationException);
    }

    [Test]
    public void RemoveIsIdempotentAndDisposesExistingHandle()
    {
        using var handles = new HandleManager();
        var handle = new TestHandle(Guid.NewGuid());
        handles.Add(handle);

        Assert.That(handles.Remove(handle.Id), Is.True);
        Assert.That(handles.Remove(handle.Id), Is.True);

        Assert.That(handle.DisposeCount, Is.EqualTo(1));
        Assert.That(handles.TryGet(handle.Id, out _), Is.False);
    }

    [Test]
    public void DisposeReleasesEveryRemainingHandle()
    {
        var first = new TestHandle(Guid.NewGuid());
        var second = new TestHandle(Guid.NewGuid());
        var handles = new HandleManager();
        handles.Add(first);
        handles.Add(second);

        handles.Dispose();
        handles.Dispose();

        Assert.That(first.DisposeCount, Is.EqualTo(1));
        Assert.That(second.DisposeCount, Is.EqualTo(1));
        Assert.That(() => handles.Add(new TestHandle(Guid.NewGuid())), Throws.TypeOf<ObjectDisposedException>());
    }

    private sealed class TestHandle : RuntimeHandle
    {
        [SetsRequiredMembers]
        public TestHandle(Guid id) => Id = id;

        public int DisposeCount { get; private set; }
        public override string TypeId => "test.handle";
        public override void Dispose() => DisposeCount++;
    }
}
