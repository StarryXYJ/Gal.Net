using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Commands;
using GalNet.Editor.Dock;
using GalNet.Editor.Models;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Shell;

internal sealed record EditorMenuCommands(ICommand Undo, ICommand Redo, ICommand TogglePanel, ICommand SaveLayout,
    ICommand LoadLayout, ICommand ShowExport, ICommand ShowProjectSettings, ICommand ShowEditorSettings);

/// <summary>Builds shell menus from commands and registered panels without owning project content.</summary>
internal sealed class EditorMenuBuilder(EditorShortcutService shortcuts, EditorDockFactory dock,
    EditorLayoutSession layouts, IProjectService projects, EditorMenuCommands commands)
{
    public AvaloniaList<MenuData> Build()
    {
        var closeCmd = shortcuts.GetCommand<CloseProjectCommand>();
        var saveCmd = shortcuts.GetCommand<SaveProjectCommand>();
        ICommand DebugAction(Func<ExternalDebugPanelViewModel, Task> action) => new AsyncRelayCommand(async () =>
        {
            if (projects.Current is not { } project) return;
            if (!dock.HasGlobalPanel(EditorDockPanelIds.ExternalDebug)) commands.TogglePanel.Execute(EditorDockPanelIds.ExternalDebug);
            await action(project.Services.GetRequiredService<ExternalDebugPanelViewModel>());
        });

        var items = new AvaloniaList<MenuData>
        {
            new()
            {
                HeaderKey = "Editor.Menu.File",
                Children = new AvaloniaList<MenuData>
                {
                    new() { HeaderKey = "Command.SaveProject", Command = saveCmd.Command, InputGesture = saveCmd.Gesture },
                    new() { HeaderKey = "Editor.Menu.Export", Command = commands.ShowExport },
                    new() { HeaderKey = "Command.CloseProject", Command = closeCmd.Command, InputGesture = closeCmd.Gesture },
                    new() { IsSeparator = true },
                    new() { HeaderKey = "Editor.Menu.Exit", InputGesture = new Avalonia.Input.KeyGesture(Key.F4, KeyModifiers.Alt), IsEnabled = false },
                }
            },
            new()
            {
                HeaderKey = "Editor.Menu.Edit",
                Children = new AvaloniaList<MenuData>
                {
                    new() { HeaderKey = "Editor.Menu.Undo", InputGesture = new Avalonia.Input.KeyGesture(Key.Z, KeyModifiers.Control), Command = commands.Undo },
                    new() { HeaderKey = "Editor.Menu.Redo", InputGesture = new Avalonia.Input.KeyGesture(Key.Y, KeyModifiers.Control), Command = commands.Redo },
                }
            },
            new()
            {
                HeaderKey = "Editor.Menu.Settings",
                Children = new AvaloniaList<MenuData>
                {
                    new() { HeaderKey = "Editor.Menu.ProjectSettings", Command = commands.ShowProjectSettings },
                    new() { HeaderKey = "Editor.Menu.EditorSettings", Command = commands.ShowEditorSettings },
                }
            },
            new()
            {
                HeaderKey = "Editor.Menu.View",
                Children = BuildViewMenuItems()
            },
            new()
            {
                HeaderKey = "Editor.Menu.Window",
                Children = new AvaloniaList<MenuData>
                {
                    new() { HeaderKey = "Uiux.Layout.Writing", Command = new RelayCommand(() => dock.ApplyWorkspacePreset("Writing")) },
                    new() { HeaderKey = "Uiux.Layout.Animation", Command = new RelayCommand(() => dock.ApplyWorkspacePreset("Animation")) },
                    new() { HeaderKey = "Uiux.Layout.Debug", Command = new RelayCommand(() => dock.ApplyWorkspacePreset("Debug")) },
                    new() { HeaderKey = "Uiux.Layout.Maximize", Command = new RelayCommand(dock.ToggleMaximizeActive) },
                    new() { HeaderKey = "Editor.Menu.SaveLayout", Command = commands.SaveLayout },
                    new() { HeaderKey = "Editor.Menu.LoadLayout", Children = BuildLoadLayoutItems() },
                    new() { HeaderKey = "Editor.Menu.DeleteLayout", Children = BuildDeleteLayoutItems() },
                }
            },
            new()
            {
                HeaderKey = "Debug.Menu",
                Children = new AvaloniaList<MenuData>
                {
                    new() { HeaderKey = "Debug.Launch", Command = DebugAction(vm => vm.LaunchCommand.ExecuteAsync(null)) },
                    new() { HeaderKey = "Debug.Wait", Command = DebugAction(vm => vm.WaitForApplicationCommand.ExecuteAsync(null)) },
                    new() { HeaderKey = "Debug.Start", Command = DebugAction(vm => vm.StartGameCommand.ExecuteAsync(null)) },
                    new() { HeaderKey = "Debug.Stop", Command = DebugAction(vm => vm.StopGameCommand.ExecuteAsync(null)) },
                    new() { HeaderKey = "Debug.GroupEnd", Command = DebugAction(vm => vm.GroupEndCommand.ExecuteAsync(null)) },
                    new() { HeaderKey = "Debug.Panel", Command = commands.TogglePanel, CommandParameter = EditorDockPanelIds.ExternalDebug }
                }
            },
            new()
            {
                HeaderKey = "Editor.Menu.Help",
                Children = new AvaloniaList<MenuData>
                {
                    new() { HeaderKey = "Editor.Menu.About", IsEnabled = false },
                }
            },
        };

        return items;
    }

    private AvaloniaList<MenuData> BuildViewMenuItems() => new(
        dock.ViewMenuPanels.Select(panel => new MenuData
        {
            HeaderKey = panel.TitleKey,
            Command = commands.TogglePanel,
            CommandParameter = panel.PanelId,
            IsCheckable = panel.IsGlobal,
            IsChecked = panel.IsGlobal && dock.HasGlobalPanel(panel.PanelId)
        }));

    private AvaloniaList<MenuData> BuildLoadLayoutItems()
    {
        var items = new AvaloniaList<MenuData> { new() { Header = "Default", Command = commands.LoadLayout, CommandParameter = "Default" } };
        foreach (var name in layouts.Names)
            items.Add(new MenuData { Header = name, Command = commands.LoadLayout, CommandParameter = name });
        return items;
    }

    private AvaloniaList<MenuData> BuildDeleteLayoutItems() => new(
        layouts.Names
            .Select(name => new MenuData { Header = name, Command = new RelayCommand<string>(layouts.Delete), CommandParameter = name }));

}
