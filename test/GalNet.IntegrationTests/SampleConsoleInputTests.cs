using GalNet.Sample.Headless;
using GalNet.Core.Execution;

namespace GalNet.IntegrationTests;

public sealed class SampleConsoleInputTests
{
    [Test]
    public async Task CancelledPromptHandsItsPendingLineToTheNextPrompt()
    {
        var reader = new PendingReader();
        var input = new ConsoleInput(reader);
        using var cancellation = new CancellationTokenSource();
        var first = input.ReadLineAsync(cancellation.Token);
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await first);
        var second = input.ReadLineAsync(CancellationToken.None);
        reader.Line.SetResult("2");
        Assert.That(await second.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo("2"));
        Assert.That(reader.ReadCount, Is.EqualTo(1));
    }

    [Test]
    public async Task WaitingPromptCanCancelWithoutStealingTheActiveRead()
    {
        var reader = new PendingReader();
        var input = new ConsoleInput(reader);
        var first = input.ReadLineAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var second = input.ReadLineAsync(cancellation.Token);
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await second);
        reader.Line.SetResult("continue");
        Assert.That(await first.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo("continue"));
        Assert.That(reader.ReadCount, Is.EqualTo(1));
    }

    [Test]
    public async Task CancelledAdvanceDoesNotConsumeTheFollowingChoice()
    {
        var reader = new PendingReader();
        var presentation = new ConsolePresentation(input: reader);
        var advances = 0;
        presentation.AdvanceRequested += () => advances++;
        using var cancellation = new CancellationTokenSource();
        await presentation.PresentTextImmediatelyAsync("", "Hello", cancellation.Token);
        cancellation.Cancel();
        var choice = ((IChoicePresenter)presentation).ChooseAsync(["one", "two"], CancellationToken.None);
        reader.Line.SetResult("2");
        Assert.That(await choice.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(1));
        Assert.That(advances, Is.Zero);
        Assert.That(reader.ReadCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ClosedInputReportsEndOfStreamForAChoice()
    {
        var reader = new PendingReader();
        var presentation = new ConsolePresentation(input: reader);
        var choice = ((IChoicePresenter)presentation).ChooseAsync(["one"], CancellationToken.None);
        reader.Line.SetResult(null);
        Assert.ThrowsAsync<EndOfStreamException>(async () => await choice.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private sealed class PendingReader : TextReader
    {
        public TaskCompletionSource<string?> Line { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReadCount { get; private set; }
        public override Task<string?> ReadLineAsync() { ReadCount++; return Line.Task; }
    }
}
