using Avalonia.Controls;
using Avalonia.Interactivity;
using DynamicLocalization.Core;
using DynamicLocalization.Core.Providers;
using GalNet.Avalonia.GameView.Services;
using GalNet.Core.Variable;
using GalNet.Runtime.Persistence;
using GalNet.Sample.Avalonia.Services;
using GalNet.Sample.Avalonia.Views;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

public sealed class SampleStartupTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp() => _root = Path.Combine(Path.GetTempPath(), "galnet-onboarding-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Test]
    public async Task ConfirmRestartAndResetPersistLocaleAndIncrementEachStartup()
    {
        using var localization = CreateLocalization();
        var startup = new SampleStartupService(localization);
        using (var variables = await FileVariableService.CreateAsync(new FilePlayerVariableStore(_root)))
        {
            var other = new Variable { Name = "player.other" };
            other.SetValue("retained");
            variables.NotifyVariableChanged(VariableScope.Player, other.Name, other);
            Assert.That(await startup.InitializeAsync(variables, "en-US", (preferred, _) =>
            {
                Assert.That(preferred, Is.EqualTo("en-US"));
                return Task.FromResult<string?>("zh-CN");
            }), Is.True);
            Assert.That(localization.CultureService.CurrentCulture.Name, Is.EqualTo("zh-CN"));
            Assert.That(variables.GetSnapshot(VariableScope.Player)[SampleStartupService.LaunchCountVariable].AsInt(), Is.EqualTo(1));
            Assert.That(variables.GetSnapshot(VariableScope.Save), Is.Empty);
        }
        using var restored = await FileVariableService.CreateAsync(new FilePlayerVariableStore(_root));
        Assert.That(await startup.InitializeAsync(restored, "en-US", (_, _) => throw new AssertionException("Picker must not reappear.")), Is.True);
        Assert.That(restored.GetSnapshot(VariableScope.Player)[SampleStartupService.LaunchCountVariable].AsInt(), Is.EqualTo(2));
        Assert.That(restored.GetSnapshot(VariableScope.Player)["player.other"].AsString(), Is.EqualTo("retained"));
        await restored.ResetPlayerVariablesAsync();
        Assert.That(await startup.InitializeAsync(restored, "en-US", (_, _) => Task.FromResult<string?>("en-US")), Is.True);
        Assert.That(restored.GetSnapshot(VariableScope.Player)[SampleStartupService.LaunchCountVariable].AsInt(), Is.EqualTo(1));
        Assert.That(localization.CultureService.CurrentCulture.Name, Is.EqualTo("en-US"));
    }

    [Test]
    public async Task CancelAndCancellationDoNotPersistOnboardingOrCount()
    {
        using var localization = CreateLocalization();
        var startup = new SampleStartupService(localization);
        using var variables = await FileVariableService.CreateAsync(new FilePlayerVariableStore(_root));
        Assert.That(await startup.InitializeAsync(variables, "en-US", (_, _) => Task.FromResult<string?>(null)), Is.False);
        Assert.ThrowsAsync<OperationCanceledException>(() => startup.InitializeAsync(variables, "en-US",
            (_, _) => Task.FromResult<string?>("zh-CN"), new CancellationToken(true)));
        Assert.That(variables.GetSnapshot(VariableScope.Player), Is.Empty);
        Assert.That(File.Exists(Path.Combine(_root, "player-variables.json")), Is.False);
    }

    [Test]
    public async Task FailedCommitDoesNotPublishLocaleOrLaunchCount()
    {
        using var localization = CreateLocalization();
        using var variables = await FileVariableService.CreateAsync(new FailedStore());
        Assert.ThrowsAsync<IOException>(() => new SampleStartupService(localization).InitializeAsync(variables, "en-US",
            (_, _) => Task.FromResult<string?>("zh-CN")));
        Assert.That(variables.GetSnapshot(VariableScope.Player), Is.Empty);
        Assert.That(localization.CultureService.CurrentCulture.Name, Is.EqualTo("en-US"));
    }

    [Test]
    public async Task SeparateProfilesAndExternalLanguagesAreSupported()
    {
        using var localization = CreateLocalization();
        localization.AttachGame(new GalNet.Runtime.Content.GameContent
        {
            Graph = new(),
            Localization = new() { ExternalSources = [new("ja-JP", "external", """{"line":"日本語"}""")] }
        });
        var startup = new SampleStartupService(localization);
        using var first = await FileVariableService.CreateAsync(new FilePlayerVariableStore(Path.Combine(_root, "first")));
        Assert.That(await startup.InitializeAsync(first, "en-US", (_, _) => Task.FromResult<string?>("ja-JP")), Is.True);
        Assert.That(localization.Resolve("line"), Is.EqualTo("日本語"));
        using var second = await FileVariableService.CreateAsync(new FilePlayerVariableStore(Path.Combine(_root, "second")));
        Assert.That(await startup.InitializeAsync(second, "en-US", (_, _) => Task.FromResult<string?>(null)), Is.False);
        Assert.That(second.GetSnapshot(VariableScope.Player), Is.Empty);
    }

    [Test]
    public void LanguageViewConfirmsCancelsAndCancelsOnCloseToken()
    {
        var view = new LanguageSelectionView();
        SettingOption[] options = [new("en-US", "English"), new("zh-CN", "简体中文")];
        var confirmed = view.ChooseAsync(options, "zh-CN", CancellationToken.None);
        view.FindControl<Button>("ConfirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(confirmed.GetAwaiter().GetResult(), Is.EqualTo("zh-CN"));
        var cancelled = view.ChooseAsync(options, "en-US", CancellationToken.None);
        view.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(cancelled.GetAwaiter().GetResult(), Is.Null);
        using var closing = new CancellationTokenSource();
        var closed = view.ChooseAsync(options, "en-US", closing.Token);
        closing.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(async () => await closed);
    }

    private static SampleLocalizationService CreateLocalization()
    {
        var provider = new JsonLocalizationProvider();
        provider.LoadJsonString("""{"line":"English"}""", "en-US");
        provider.LoadJsonString("""{"line":"中文"}""", "zh-CN");
        var culture = new CultureService();
        culture.RegisterProvider(provider);
        culture.SetCulture("en-US");
        return new SampleLocalizationService(culture, [provider]);
    }

    private sealed class FailedStore : IPlayerVariableStore
    {
        public Task<IReadOnlyDictionary<string, Variable>> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, Variable>>(new Dictionary<string, Variable>());
        public Task SaveAsync(IReadOnlyDictionary<string, Variable> variables, CancellationToken ct = default) =>
            throw new IOException("Simulated startup write failure.");
    }
}
