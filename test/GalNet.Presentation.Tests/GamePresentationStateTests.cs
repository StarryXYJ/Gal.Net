using GalNet.Avalonia.GameView.Composition;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Presentation;

public sealed class GamePresentationStateTests
{
    [Test]
    public void DefaultPageAndRuntimeShareOneStatePerScope()
    {
        var services = new ServiceCollection();
        services.AddScoped<GamePresentationState>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var state = first.ServiceProvider.GetRequiredService<GamePresentationState>();
        Assert.That(state.GetType().BaseType, Is.EqualTo(typeof(CommunityToolkit.Mvvm.ComponentModel.ObservableObject)));
        Assert.That(second.ServiceProvider.GetRequiredService<GamePresentationState>(), Is.Not.SameAs(state));
    }

    [Test]
    public async Task StandaloneStateCancelsAndReusesInteractionWithoutAnyPageOrNavigation()
    {
        var state = new GamePresentationState();
        using var cancellation = new CancellationTokenSource();
        var canceledChoice = state.WaitForChoiceAsync(new[] { "Old choice" }, cancellation.Token);
        cancellation.Cancel();
        try { await canceledChoice; Assert.Fail("The canceled choice must finish."); }
        catch (OperationCanceledException) { }
        Assert.That(state.IsChoiceVisible, Is.False);
        Assert.That(state.Choices, Is.Empty);

        var choice = state.WaitForChoiceAsync(new[] { "First", "Second" }, CancellationToken.None);
        state.CompleteChoiceCommand.Execute(1);
        Assert.That(await choice, Is.EqualTo(1));
        Assert.That(state.IsChoiceVisible, Is.False);

        var advance = state.WaitForAdvanceAsync(CancellationToken.None);
        state.CompleteAdvance();
        await advance;
    }
}
