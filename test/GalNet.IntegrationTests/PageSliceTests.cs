using System.Collections.ObjectModel;
using GalNet.Sample.CrossPlatform.ViewModels;

namespace GalNet.IntegrationTests;

public sealed class PageSliceTests
{
    [Test]
    public void PageBoundariesAndSourceUpdatesKeepBorrowedItems()
    {
        var source = new ObservableCollection<int>(Enumerable.Range(0, 25));
        using var pages = new PageSlice<int>(source, 12);
        Assert.That(pages.Items, Is.EqualTo(Enumerable.Range(0, 12)));
        Assert.That(pages.HasMultiplePages, Is.True);
        pages.CurrentPage = 3;
        Assert.That(pages.Items, Is.EqualTo(new[] { 24 }));
        source.RemoveAt(24);
        Assert.That(pages.CurrentPage, Is.EqualTo(2));
        Assert.That(pages.Items, Is.EqualTo(Enumerable.Range(12, 12)));
        source[12] = 99;
        Assert.That(pages.Items[0], Is.EqualTo(99));
        source.Clear();
        Assert.That(pages.CurrentPage, Is.EqualTo(1));
        Assert.That(pages.HasMultiplePages, Is.False);
        Assert.That(pages.Items, Is.Empty);
    }

    [Test]
    public void RebuildingTheSourceKeepsThePageWhenItStillExists()
    {
        var source = new ObservableCollection<int>(Enumerable.Range(0, 30));
        using var pages = new PageSlice<int>(source, 12) { CurrentPage = 2 };
        pages.UpdateSource(() =>
        {
            source.Clear();
            foreach (var value in Enumerable.Range(100, 30)) source.Add(value);
        });
        Assert.That(pages.CurrentPage, Is.EqualTo(2));
        Assert.That(pages.Items, Is.EqualTo(Enumerable.Range(112, 12)));
        pages.Dispose();
        source.Add(200);
        Assert.That(pages.Items, Is.Empty);
    }
}
