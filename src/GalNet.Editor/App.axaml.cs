using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Dock.Model.Mvvm;
using GalNet.Control.Services;
using GalNet.Control.View;
using GalNet.Control.ViewModels;
using GalNet.Control.Views;
using GalNet.Core.Services;
using GalNet.Editor.Commands;
using GalNet.Editor.Dock;
using GalNet.Editor.Project;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
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

        // ── 编辑器级 Singleton ──
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IEditorSettingsService, EditorSettingsService>();

        // Game exit — editor does nothing on Quit
        services.AddSingleton<IGameExitService, EditorGameExitService>();

        // ── Dock 布局工厂（Singleton - 每次创建新 Layout） ──
        services.AddSingleton<EditorDockFactory>();

        // ── 命令系统 ──
        services.AddSingleton<CommandService>();
        services.AddSingleton<SaveProjectCommand>();
        services.AddSingleton<CloseProjectCommand>();

        // Navigation (singleton — shared across all pages)
        services.AddSingleton<INavigationService>(sp =>
        {
            var nav = new NavigationService(sp);
            // Register VM→View mappings
            nav.RegisterMap(typeof(StartupPageViewModel), typeof(StartupPageView));
            nav.RegisterMap(typeof(EditorPageViewModel), typeof(EditorPageView));
            nav.RegisterMap(typeof(GamePageHostViewModel), typeof(GamePageHostView));
            return nav;
        });

        // Views (注册到 DI 以便从容器解析)
        services.AddTransient<StartupPageView>();
        services.AddTransient<EditorPageView>();
        services.AddTransient<GamePageHostView>();

        // Game view (transient — 每次开始游戏创建新实例)
        services.AddTransient<DefaultGameView>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            return new DefaultGameView(settings.GetSnapshot());
        });

        // Game host and sub-pages
        services.AddTransient<GamePageHostViewModel>();
        services.AddTransient<GameStartViewModel>();
        services.AddTransient<GameRunViewModel>();

        // Pages / ViewModels
        services.AddTransient<StartupPageViewModel>();
        services.AddTransient<EditorPageViewModel>();

        // ── 编辑器面板（transient — 每次打开编辑器页面创建新实例） ──
        services.AddTransient<ProjectSettingsPanelViewModel>();
        services.AddTransient<EditorSettingsPanelViewModel>();
        services.AddTransient<GamePreviewPanelViewModel>();

        // Window + ViewModel
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        // ── 设置窗口（transient — 每次弹出创建新实例） ──
        services.AddTransient<ProjectSettingsWindow>();
        services.AddTransient<EditorSettingsWindow>();

        ServiceProvider = services.BuildServiceProvider();

        // ── Launch ──
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
