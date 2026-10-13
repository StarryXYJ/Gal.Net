using GalNet.ProjectTools.Windows;

namespace GalNet.Editor.Shared.Tests;

public sealed class WindowsRecycleGuardTests
{
    [Test]
    public void PermanentDeleteProposal_IsVetoedBeforeWindowsTouchesFiles()
    {
        var sink = new WindowsProjectRecycleBin.RecycleSink(default);
        Assert.That(sink.PreDeleteItem(0, null!), Is.LessThan(0));
        Assert.That(sink.RejectedPermanentDelete, Is.True);
        Assert.That(sink.Completed, Is.False);
    }

    [Test]
    public void Cancellation_VetoesPendingRecycle_ButCompletedRecycleIsRecorded()
    {
        var sink = new WindowsProjectRecycleBin.RecycleSink(new(true));
        Assert.That(sink.PreDeleteItem(0x80, null!), Is.LessThan(0));
        Assert.That(sink.RejectedPermanentDelete, Is.False);
        Assert.That(sink.Completed, Is.False);
        sink.PostDeleteItem(0x80, null!, 0, null);
        Assert.That(sink.Completed, Is.True);
    }
}
