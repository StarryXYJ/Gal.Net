extern alias GameViewAssembly;
using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GalNet.Core.Scene;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Composition;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GameViewAssembly::GalNet.Avalonia.GameView.Presentation;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor;

public partial class App : Application
{
    /// <summary>DI container for application-level services.</summary>
    public static IServiceProvider? ServiceProvider { get; private set; }

    public static ISettingsService GetSettingsService() =>
        ServiceProvider!.GetRequiredService<ISettingsService>();

    public override void Initialize()
    {
#if BENCHMARK
        using var measurement = GalNet.Diagnostics.CpuProfiler.Shared.Measure("Editor.Xaml");
#endif
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
#if BENCHMARK
        using var frameworkMeasurement = GalNet.Diagnostics.CpuProfiler.Shared.Measure("Editor.FrameworkInitialization");
#endif
        var services = new ServiceCollection();
#if BENCHMARK
        using (GalNet.Diagnostics.CpuProfiler.Shared.Measure("Editor.RegisterServices"))
#endif
        {
            services.AddSingleton<IEffectCatalog>(_ => AvaloniaEffectRuntime.CreateDefaultCatalog());
            services.AddEditorServices();
        }

        {
#if BENCHMARK
            using var measurement = GalNet.Diagnostics.CpuProfiler.Shared.Measure("Editor.BuildServiceProvider");
#endif
            ServiceProvider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        }

        {
#if BENCHMARK
            using var measurement = GalNet.Diagnostics.CpuProfiler.Shared.Measure("Editor.SettingsLocaleTheme");
#endif
            var editorSettings = ServiceProvider.GetRequiredService<IEditorSettingsService>().GetSettings();
            ServiceProvider.GetRequiredService<IEditorLocalizationService>().ApplyLocale(editorSettings.UiLocale.Code);
            ServiceProvider.GetRequiredService<IThemeService>().ApplyThemeByName(editorSettings.Theme);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
#if BENCHMARK
            using (GalNet.Diagnostics.CpuProfiler.Shared.Measure("Editor.CreateMainWindow"))
#endif
                desktop.MainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            desktop.Exit += (_, _) =>
            {
                var navigation = ServiceProvider.GetRequiredService<INavigationService>();
                (navigation.CurrentPage as EditorPageViewModel)?.PersistLayoutNow();
                if (ServiceProvider is IDisposable disposable)
                    disposable.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
