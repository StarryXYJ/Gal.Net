using GalNet.Core.Primitives;

namespace GeneralTest.Primitives;

public class PrimitiveInvocationTests
{
    [TestCase(PrimitiveInvocationMode.Normal)]
    [TestCase(PrimitiveInvocationMode.Quick)]
    [TestCase(PrimitiveInvocationMode.CatchUp)]
    public void DefaultModesExecuteLogicOnce(PrimitiveInvocationMode mode)
    {
        var instance = new ControlledInstance();
        instance.Dispatch(mode);
        instance.ApplyPlaybackMode(PrimitiveInvocationMode.Normal);
        instance.ApplyPlaybackMode(PrimitiveInvocationMode.Quick);
        Assert.That(instance.DispatchCount, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => instance.Dispatch(mode));
    }

    [Test]
    public void AutoReadinessIsIndependentOfCompletionAndNotifiesOnlyChanges()
    {
        var instance = new ControlledInstance();
        var notifications = 0;
        instance.AutoBlockingChanged += _ => notifications++;
        instance.Dispatch();
        instance.SetReady(true);
        instance.SetReady(true);
        Assert.Multiple(() =>
        {
            Assert.That(instance.IsAutoBlocking, Is.False);
            Assert.That(instance.IsCompleted, Is.False);
            Assert.That(notifications, Is.EqualTo(1));
        });
        instance.SetReady(false);
        instance.Finish();
        instance.Finish();
        Assert.That(instance.IsAutoBlocking, Is.False);
        Assert.That(notifications, Is.EqualTo(3));
    }

    private sealed class ControlledInstance : PrimitiveInstance
    {
        public override bool IsBlocking => true;
        public override bool IsSkippable => true;
        public int DispatchCount { get; private set; }
        protected override void OnDispatch() => DispatchCount++;
        public void SetReady(bool ready) => SetAutoBlocking(!ready);
        public void Finish() => TryComplete();
    }
}
