using System.Reflection;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameHost;
using GalNet.Sample.CrossPlatform;
using GalNet.Sample.CrossPlatform.Opening;
using GalNet.Sample.CrossPlatform.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class SampleOpeningTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static Task Present(OpeningPageViewModel page, CancellationToken token) => Task.CompletedTask;

    [Test]
    public async Task SkipCancelsAndJoinsPageWorkBeforeStartingNextAndIgnoresLateFinish()
    {
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Probe("first") { Cleanup = cleanup.Task };
        var second = new Probe("second");
        await using var sequence = new OpeningSequence([() => first, () => second]);
        var run = sequence.RunAsync(Present);
        await first.Started.Task.WaitAsync(Deadline);
        sequence.SkipCurrentCommand.Execute(null);
        await first.Stopping.Task.WaitAsync(Deadline);
        Assert.That(first.WorkCancelled, Is.True);
        Assert.That(second.Started.Task.IsCompleted, Is.False, "Cleanup must complete before the next page starts.");
        cleanup.SetResult();
        await second.Started.Task.WaitAsync(Deadline);
        first.End(); first.End();
        Assert.That(sequence.CurrentPage, Is.SameAs(second));
        Assert.That(run.IsCompleted, Is.False);
        second.End(); second.End();
        await run.WaitAsync(Deadline);
        await first.DisposeAsync();
        Assert.That(first.Stops, Is.EqualTo(1));
        Assert.That(second.Stops, Is.EqualTo(1));
        Assert.That(sequence.CurrentPage, Is.Null);
    }

    [Test]
    public async Task SkipAllPreservesUnskippablePageAndDisposesUnshownPages()
    {
        var first = new Probe("first");
        var notice = new Probe("notice", canSkip: false);
        var last = new Probe("last");
        await using var sequence = new OpeningSequence([() => first, () => notice, () => last]);
        var run = sequence.RunAsync(Present);
        await first.Started.Task.WaitAsync(Deadline);
        sequence.SkipRemainingCommand.Execute(null);
        await notice.Started.Task.WaitAsync(Deadline);
        sequence.SkipCurrentCommand.Execute(null);
        Assert.That(run.IsCompleted, Is.False);
        Assert.That(sequence.CurrentPage, Is.SameAs(notice));
        notice.End();
        await run.WaitAsync(Deadline);
        Assert.That(last.Started.Task.IsCompleted, Is.False);
        Assert.That(last.Stops, Is.EqualTo(1));
    }

    [Test]
    public async Task DisposalCancelsPendingTimerAndIsIdempotent()
    {
        var page = new Probe("timer");
        var sequence = new OpeningSequence([() => page]);
        var run = sequence.RunAsync(Present);
        await page.Started.Task.WaitAsync(Deadline);
        var disposal = sequence.DisposeAsync().AsTask();
        await disposal.WaitAsync(Deadline);
        await sequence.DisposeAsync();
        Assert.That(page.WorkCancelled, Is.True);
        Assert.That(page.Stops, Is.EqualTo(1));
        Assert.That(sequence.IsRunning, Is.False);
        Assert.That(sequence.CurrentPage, Is.Null);
        Assert.That(run.IsCanceled, Is.True);
    }

    [Test]
    public async Task FailedPageStillCleansUpAndDoesNotStartTheNextPage()
    {
        var page = new Probe("bad") { Fail = true };
        var next = new Probe("next");
        var sequence = new OpeningSequence([() => page, () => next]);
        var run = sequence.RunAsync(Present);
        var failure = Assert.ThrowsAsync<AggregateException>(async () => await run.WaitAsync(Deadline));
        Assert.That(failure!.Flatten().InnerExceptions.Single(), Is.TypeOf<IOException>());
        Assert.That(page.Stops, Is.EqualTo(1));
        Assert.That(next.Started.Task.IsCompleted, Is.False);
        Assert.That(sequence.CurrentPage, Is.Null);
        Assert.That(sequence.IsRunning, Is.False);
        Assert.ThrowsAsync<AggregateException>(async () => await sequence.DisposeAsync());
    }

    [Test]
    public async Task OnceHistoryIsProfileScopedAndDoesNotRecordUnshownOrCancelledPages()
    {
        var profile = Path.Combine(Path.GetTempPath(), "galnet-opening-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new Probe("once", OpeningShowPolicy.OncePerProfile) { AutoFinish = true };
            await using (var sequence = new OpeningSequence([() => first]))
            {
                sequence.SetProfileDirectory(profile);
                await sequence.RunAsync(Present).WaitAsync(Deadline);
            }
            var restored = new Probe("once", OpeningShowPolicy.OncePerProfile);
            var cancelled = new Probe("cancelled", OpeningShowPolicy.OncePerProfile);
            var unshown = new Probe("unshown", OpeningShowPolicy.OncePerProfile);
            await using (var sequence = new OpeningSequence([() => restored, () => cancelled, () => unshown]))
            {
                sequence.SetProfileDirectory(profile);
                var run = sequence.RunAsync(Present);
                await cancelled.Started.Task.WaitAsync(Deadline);
                await sequence.DisposeAsync();
                Assert.That(restored.Started.Task.IsCompleted, Is.False);
                Assert.That(run.IsCanceled, Is.True);
                Assert.That(await File.ReadAllLinesAsync(Path.Combine(profile, "opening-pages.txt")), Is.EqualTo(new[] { "once" }));
            }
        }
        finally { if (Directory.Exists(profile)) Directory.Delete(profile, true); }
    }

    [Test]
    public Task FirstLanguagePickerWaitsUntilOpeningEndsAndCanBeCancelled() => SampleUiTests.RunAsync(async () =>
    {
        var page = new Probe("logo");
        await using var sequence = new OpeningSequence([() => page]);
        var run = sequence.RunAsync(Present);
        await using var host = await AvaloniaPlayerHost.CreateAsync(SampleCustomization.CreateHostOptions(), services =>
        {
            SampleCustomization.ConfigureServices(services);
            services.AddSingleton(sequence);
        });
        var view = host.Services.GetRequiredService<PlayerView>();
        var application = host.Services.GetRequiredService<IPlayerApplication>();
        using var cancellation = new CancellationTokenSource();
        var selecting = application.SelectLanguageAsync("en-US", cancellation.Token);
        Assert.That(view.FindControl<Control>("LanguageSelection")!.IsVisible, Is.False);
        sequence.SkipRemainingCommand.Execute(null);
        await run.WaitAsync(Deadline);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.That(view.FindControl<Control>("LanguageSelection")!.IsVisible, Is.True);
        cancellation.Cancel();
        try { await selecting; Assert.Fail("The language picker must observe cancellation."); }
        catch (OperationCanceledException) { }
        Assert.That(view.FindControl<Control>("LanguageSelection")!.IsVisible, Is.False);
    });

    [Test]
    public Task SamplePagesRenderRealPngAndNoticesAndSkipWithoutWaitingForFadeOut() => SampleUiTests.RunAsync(async () =>
    {
        using var localization = SampleCustomization.CreateLocalizationService();
        localization.ApplyCulture("zh-CN");
        await using var sequence = SampleCustomization.CreateOpeningSequence(key => localization[key]);
        var view = new OpeningSequenceView { DataContext = sequence };
        view.Resources.MergedDictionaries.Add(SampleCustomization.PlayerPalette.CreateResources());
        view.Styles.Add(SampleCustomization.CreateStyles());
        var window = new Window { Width = 1280, Height = 720, Content = view };
        window.Show();
        try
        {
            var run = sequence.RunAsync(view.PresentAsync);
            await Task.Delay(800);
            window.UpdateLayout();
            var logo = window.GetVisualDescendants().OfType<EngineLogoPage>().Single();
            Assert.That(logo.Opacity, Is.GreaterThan(0.95));
            Assert.That(logo.GetVisualDescendants().OfType<Image>().Single().Source, Is.Not.Null);
            Capture(window, "opening-logo.png");
            // A held key may skip only the current page, even when OS key repeat fires.
            view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            await Task.Delay(100);
            view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.That(sequence.CurrentPage, Is.TypeOf<DisclaimerPageViewModel>());
            await Task.Delay(700);
            window.UpdateLayout();
            var notice = window.GetVisualDescendants().OfType<DisclaimerPage>().Single();
            Assert.That(notice.Opacity, Is.GreaterThan(0.95));
            Assert.That(notice.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text!.Contains("虚构", StringComparison.Ordinal)), Is.True);
            Capture(window, "opening-notice.png");
            Assert.That(view.GetVisualDescendants().OfType<Button>(), Is.Empty, "Opening pages have no skip-button overlay.");
            var point = new Point(window.ClientSize.Width / 2, window.ClientSize.Height / 2);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            await run.WaitAsync(Deadline);
            Assert.That(sequence.IsRunning, Is.False);
            Assert.That(view.IsVisible, Is.False);
        }
        finally { window.Close(); }
    });

    [Test]
    public Task BothSamplePagesFinishNaturallyInOrder() => SampleUiTests.RunAsync(async () =>
    {
        using var localization = SampleCustomization.CreateLocalizationService();
        await using var sequence = SampleCustomization.CreateOpeningSequence(key => localization[key]);
        var view = new OpeningSequenceView { DataContext = sequence };
        view.Resources.MergedDictionaries.Add(SampleCustomization.PlayerPalette.CreateResources());
        view.Styles.Add(SampleCustomization.CreateStyles());
        var window = new Window { Width = 1280, Height = 720, Content = view };
        var presented = new List<(string Id, TimeSpan At)>();
        var watch = Stopwatch.StartNew();
        window.Show();
        try
        {
            await sequence.RunAsync(async (page, token) =>
            {
                await view.PresentAsync(page, token);
                presented.Add((page.Id, watch.Elapsed));
            }).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(presented.Select(item => item.Id), Is.EqualTo(new[] { "galnet-logo", "play-notice" }));
            Assert.That(presented[1].At - presented[0].At, Is.GreaterThan(TimeSpan.FromSeconds(1.8)));
            Assert.That(watch.Elapsed - presented[1].At, Is.GreaterThan(TimeSpan.FromSeconds(1.8)));
            Assert.That(sequence.CurrentPage, Is.Null);
            Assert.That(sequence.IsRunning, Is.False);
        }
        finally { window.Close(); }
    });

    [Test]
    public Task OpeningAppearsBeforePlatformPreparationAndShutdownJoinsIt() => SampleUiTests.RunAsync(async () =>
    {
        var previous = App.PrepareLaunchAsync;
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = new App();
        var fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var window = new Window();
        try
        {
            App.PrepareLaunchAsync = async token =>
            {
                preparing.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("Cancelled preparation cannot publish paths.");
            };
            window.Content = typeof(App).GetMethod("CreateHostView", fields)!.Invoke(app, null);
            window.Show();
            await preparing.Task.WaitAsync(Deadline);
            Assert.That(typeof(App).GetField("_player", fields)!.GetValue(app), Is.Null);
            Assert.That(window.GetVisualDescendants().OfType<EngineLogoPage>().Any(), Is.True);
            var sequence = (OpeningSequence)typeof(App).GetField("_opening", fields)!.GetValue(app)!;
            await app.ShutdownAsync().WaitAsync(Deadline);
            Assert.That(sequence.IsRunning, Is.False);
            Assert.That(sequence.CurrentPage, Is.Null);
        }
        finally { window.Close(); App.PrepareLaunchAsync = previous; }
    });

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("GALNET_THEME_CAPTURE_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bitmap = window.CaptureRenderedFrame();
        bitmap!.Save(Path.Combine(directory, name));
    }

    private sealed class Probe(string id, OpeningShowPolicy policy = OpeningShowPolicy.EveryLaunch, bool canSkip = true) : OpeningPageViewModel
    {
        public override string Id => id;
        public override OpeningShowPolicy ShowPolicy => policy;
        public override bool CanSkip => canSkip;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopping { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Cleanup { get; init; } = Task.CompletedTask;
        public bool AutoFinish { get; init; }
        public bool Fail { get; init; }
        public bool WorkCancelled { get; private set; }
        public int Stops { get; private set; }
        public void End() => Finish();
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (Fail) throw new IOException("Opening failed.");
            if (AutoFinish) { Finish(); return; }
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            finally { WorkCancelled = cancellationToken.IsCancellationRequested; }
        }
        protected override async ValueTask StopAsync()
        {
            Stops++;
            Stopping.TrySetResult();
            await Cleanup;
        }
    }
}
