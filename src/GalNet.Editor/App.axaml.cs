using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GalNet.Core.Services;
using GalNet.Editor.Composition;
using GalNet.Editor.Services;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor;

public partial class App : Application
{
    /// <summary>DI container for application-level services.</summary>
    public static IServiceProvider? ServiceProvider { get; private set; }

    public ISettingsService GetSettingsService() =>
        ServiceProvider!.GetRequiredService<ISettingsService>();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        services.AddEditorServices();

        ServiceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var editorSettings = ServiceProvider.GetRequiredService<IEditorSettingsService>().GetSettings();
        ServiceProvider.GetRequiredService<IEditorLocalizationService>().ApplyLocale(editorSettings.UiLocale.Code);
        ServiceProvider.GetRequiredService<IThemeService>().ApplyThemeByName(editorSettings.Theme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            desktop.Exit += (_, _) =>
            {
                if (ServiceProvider is IDisposable disposable)
                    disposable.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
