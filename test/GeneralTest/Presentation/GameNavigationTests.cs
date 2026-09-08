using Avalonia.Controls;
using GalNet.Avalonia.GameView.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Presentation;

public sealed class GameNavigationTests
{
    [Test]
    public void Navigation_reuses_scoped_view_models_and_preserves_history()
    {
        using var provider = CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var navigation = services.GetRequiredService<IGameNavigationService>();

        navigation.ResetTo<FirstPage>();
        var first = navigation.CurrentViewModel;
        navigation.Navigate<SecondPage>();

        Assert.Multiple(() =>
        {
            Assert.That(navigation.CanGoBack, Is.True);
            Assert.That(navigation.CurrentViewModel, Is.SameAs(services.GetRequiredService<SecondPage>()));
        });

        navigation.GoBack();
        Assert.That(navigation.CurrentViewModel, Is.SameAs(first));
    }

    [Test]
    public async Task Parameter_activation_completes_before_navigation()
    {
        using var provider = CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var navigation = scope.ServiceProvider.GetRequiredService<IGameNavigationService>();

        await navigation.NavigateAsync<ActivatedPage, string>("payload");

        var page = (ActivatedPage)navigation.CurrentViewModel!;
        Assert.That(page.Argument, Is.EqualTo("payload"));
    }

    [Test]
    public void Registry_override_controls_the_view_mapping_and_scope_reuses_the_view()
    {
        var registry = new PageViewRegistry();
        registry.Register<FirstPage, FirstView>();
        registry.Register<FirstPage, ReplacementView>();
        var services = CreateServices();
        services.AddSingleton<IPageViewRegistry>(registry);
        services.AddScoped<IPageViewFactory, PageViewFactory>();
        services.AddScoped<FirstView>();
        services.AddScoped<ReplacementView>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IPageViewFactory>();
        var page = scope.ServiceProvider.GetRequiredService<FirstPage>();

        var first = factory.Create(page);
        var second = factory.Create(page);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.TypeOf<ReplacementView>());
            Assert.That(second, Is.SameAs(first));
            Assert.That(first.DataContext, Is.SameAs(page));
        });
    }

    [Test]
    public void Disposing_scope_disposes_resolved_page_view_models()
    {
        DisposablePage.DisposeCount = 0;
        using var provider = CreateServices().BuildServiceProvider();
        var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IGameNavigationService>().ResetTo<DisposablePage>();

        scope.Dispose();

        Assert.That(DisposablePage.DisposeCount, Is.EqualTo(1));
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddScoped<IGameNavigationService, GameNavigationService>();
        services.AddScoped<FirstPage>();
        services.AddScoped<SecondPage>();
        services.AddScoped<ActivatedPage>();
        services.AddScoped<DisposablePage>();
        return services;
    }

    private sealed class FirstPage : PageViewModelBase;
    private sealed class SecondPage : PageViewModelBase;
    private sealed class ActivatedPage : PageViewModelBase, IActivatablePageViewModel<string>
    {
        public string? Argument { get; private set; }
        public Task ActivateAsync(string args, CancellationToken cancellationToken = default)
        {
            Argument = args;
            return Task.CompletedTask;
        }
    }
    private sealed class DisposablePage : PageViewModelBase, IDisposable
    {
        public static int DisposeCount { get; set; }
        public void Dispose() => DisposeCount++;
    }
    private sealed class FirstView : Control;
    private sealed class ReplacementView : Control;
}
