using GalNet.Presentation.Abstractions.Navigation;

namespace GeneralTest.Presentation;

public sealed class NavigationHistoryTests
{
    [Test]
    public void Push_back_reset_and_clear_share_one_history_semantics()
    {
        var history = new TestHistory();

        history.Navigate("title");
        history.Navigate("settings");
        history.GoBack();
        history.Reset("game");
        history.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(history.CurrentPage, Is.Null);
            Assert.That(history.CanGoBack, Is.False);
            Assert.That(history.Changes, Is.EqualTo(["title", "settings", "title", "game", null]));
        });
    }

    private sealed class TestHistory : NavigationHistory<string>
    {
        public List<string?> Changes { get; } = [];
        public string? CurrentPage => Current;

        public void Navigate(string page) => Push(page);
        public void Reset(string page) => Replace(page);
        public void GoBack() => TryGoBack();
        public void Clear() => ClearCurrent();

        protected override void OnCurrentChanged(string? page) => Changes.Add(page);
    }
}
