using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using GalNet.Control.Services;
using GalNet.Control.View;
using GalNet.Control.ViewModels;
using GalNet.Control.Views;
using GalNet.Core.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor;

public partial class App : Application
{
    /// <summary>DI 容器，供代码按需解析。</summary>
    public static IServiceProvider? ServiceProvider { get; private set; }

    /// <summary>从 DI 容器获取设置服务。</summary>
    public ISettingsService GetSettingsService() =>
        ServiceProvider!.GetRequiredService<ISettingsService>();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // ── Build DI container ──
        var services = new ServiceCollection();

        // Core services
        services.AddSingleton<ISettingsService, SettingsService>();

        // Navigation (singleton — shared across all pages)
        services.AddSingleton<INavigationService>(sp =>
        {
            var nav = new NavigationService(sp);
            // Register VM→View mappings
            nav.RegisterMap(typeof(GamePageHostViewModel), typeof(GamePageHostView));
            return nav;
        });

        // Game view (transient — 每次开始游戏创建新实例，无需手动重置)
        services.AddTransient<DefaultGameView>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            return new DefaultGameView(settings.GetSnapshot());
        });

        // Game host and sub-pages
        services.AddTransient<GamePageHostViewModel>();
        services.AddTransient<GameStartViewModel>();
        services.AddTransient<GameRunViewModel>();

        // Window + ViewModel
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        ServiceProvider = services.BuildServiceProvider();

        // ── Launch ──
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
