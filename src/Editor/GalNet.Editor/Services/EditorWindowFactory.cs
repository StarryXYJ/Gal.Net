using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

public sealed class EditorWindowFactory : IEditorWindowFactory
{
    private readonly IProjectService _projects;

    public EditorWindowFactory(IProjectService projects)
    {
        _projects = projects;
    }

    private IServiceProvider ProjectServices =>
        (_projects.Current ?? throw new InvalidOperationException("An editor window requires an open project."))
        .Services;

    public ProjectSettingsWindow CreateProjectSettingsWindow()
    {
        var window = ProjectServices.GetRequiredService<ProjectSettingsWindow>();
        window.DataContext = ProjectServices.GetRequiredService<ProjectSettingsPanelViewModel>();
        return window;
    }

    public EditorSettingsWindow CreateEditorSettingsWindow()
    {
        var window = ProjectServices.GetRequiredService<EditorSettingsWindow>();
        window.DataContext = ProjectServices.GetRequiredService<EditorSettingsPanelViewModel>();
        return window;
    }

    public ExportWindow CreateExportWindow()
    {
        var window = ProjectServices.GetRequiredService<ExportWindow>();
        window.DataContext = ProjectServices.GetRequiredService<ExportPanelViewModel>();
        return window;
    }
    public async Task<string?> RequestLayoutNameAsync(IEditorLocalizationService L)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return null;
        var input = new TextBox { MinWidth = 260, PlaceholderText = L["Layout.Name"] };
        var dialog = new Window
        {
            Title = L["Editor.Menu.SaveLayout"],
            Width = 360,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                input,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        new Button { Content = L["Common.Cancel"], Command = new RelayCommand(() => dialog.Close(null)) },
                        new Button { Content = L["Common.Save"], Command = new RelayCommand(() => dialog.Close(input.Text)) }
                    }
                }
            }
        };
        var bindings = new List<IDisposable> { LocalizedBindings.Bind(dialog, Window.TitleProperty, L, "Editor.Menu.SaveLayout"), LocalizedBindings.Bind(input, TextBox.PlaceholderTextProperty, L, "Layout.Name") };
        foreach (var button in ((StackPanel)dialog.Content!).Children.OfType<StackPanel>().SelectMany(panel => panel.Children).OfType<Button>())
            bindings.Add(LocalizedBindings.Bind(button, ContentControl.ContentProperty, L, button.Content?.ToString() == L["Common.Cancel"] ? "Common.Cancel" : "Common.Save"));
        dialog.Closed += (_, _) => { foreach (var binding in bindings) binding.Dispose(); };
        return await dialog.ShowDialog<string?>(owner);
    }
}
