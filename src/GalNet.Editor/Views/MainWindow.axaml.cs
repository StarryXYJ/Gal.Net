using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using GalNet.Control.Host;
using GalNet.Control.Screen.BuiltIn;
using GalNet.Control.Services;
using GalNet.Control.View;
using GalNet.Control.Widget.BuiltIn;
using GalNet.Core.Services;
using Serilog;

namespace GalNet.Editor.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        StatusText.Text = "Loading...";
        Log.Information("=== GalNet Editor MVP starting ===");

        // ── Services ──
        var settingsService = new SettingsService();
        var gameView = new DefaultGameView(settingsService.GetSnapshot());

        // ── Host view ──
        GameHost.RegisterNavigateTo("Title", () =>
        {
            var view = new TitleScreenView();
            view.SetTitle("GalNet Demo");
            view.SetButtons(new[] { "New Game", "Continue", "Settings", "Quit" });
            view.ButtonClicked += (index) =>
            {
                switch (index)
                {
                    case 0: // New Game
                        GameHost.NavigateTo("Game");
                        _ = RunGameAsync(gameView, settingsService);
                        break;
                    case 1: // Continue
                        GameHost.ShowToast("No save data");
                        break;
                    case 2: // Settings
                        GameHost.ShowModal("Settings");
                        break;
                    case 3: // Quit
                        Close();
                        break;
                }
            };
            return view;
        });

        // Game screen
        GameHost.RegisterNavigateTo("Game", () =>
        {
            StatusText.Text = "Playing...";
            return gameView;
        });

        // Settings modal with ViewModel
        GameHost.RegisterModal("Settings", () =>
        {
            var settingsVm = new SettingsScreenViewModel(settingsService, GameHost);
            var settingsView = new SettingsScreenView();
            settingsView.BindToViewModel(settingsVm);
            return settingsView;
        });

        // ── Show title screen ──
        StatusText.Text = "Title screen";
        GameHost.NavigateTo("Title");
    }

    private async Task RunGameAsync(DefaultGameView gameView, SettingsService settingsService)
    {
        // Must run engine on background thread to avoid deadlocking UI
        await Task.Run(async () =>
        {
            var sampleDir = Path.Combine(AppContext.BaseDirectory, "sample");
            Directory.CreateDirectory(sampleDir);

            File.WriteAllText(Path.Combine(sampleDir, "graph.json"), """
            {
              "version": 1, "name": "DemoScene", "rootNodeId": "start",
              "nodes": [
                { "id": "start", "type": "Group", "name": "start", "x": 100, "y": 220 },
                { "id": "branch", "type": "Branch", "name": "choice", "x": 350, "y": 220,
                  "branchType": "Choice",
                  "options": [
                    { "text": "Go left", "condition": "" },
                    { "text": "Go right", "condition": "" }
                  ]
                },
                { "id": "left", "type": "Group", "name": "left", "x": 600, "y": 120 },
                { "id": "right", "type": "Group", "name": "right", "x": 600, "y": 320 }
              ],
              "edges": [
                { "fromNodeId": "start", "fromOutlet": 0, "toNodeId": "branch" },
                { "fromNodeId": "branch", "fromOutlet": 0, "toNodeId": "left" },
                { "fromNodeId": "branch", "fromOutlet": 1, "toNodeId": "right" }
              ]
            }
            """);

            File.WriteAllText(Path.Combine(sampleDir, "start.galgroup"),
                "text : speaker:; content:Hello from GalNet!\\d1000Let's make a choice.");

            File.WriteAllText(Path.Combine(sampleDir, "left.galgroup"),
                "text : content:You chose the left path.\\njump : type:end");

            File.WriteAllText(Path.Combine(sampleDir, "right.galgroup"),
                "text : content:You chose the right path.\\njump : type:end");

            var graph = GalNet.Runtime.Loader.GraphLoader.LoadFromFile(
                Path.Combine(sampleDir, "graph.json"));

            foreach (var group in graph.Nodes.OfType<GalNet.Core.Graph.Group>())
            {
                var path = Path.Combine(sampleDir, $"{group.Id}.galgroup");
                if (File.Exists(path))
                    GalNet.Runtime.Loader.GalgroupLoader.LoadIntoGroupFromContent(
                        group, File.ReadAllText(path));
            }

            var settings = new GalNet.Core.Settings.SettingsContainer();
            settings.Set(settingsService.GetSnapshot());

            var engine = new GalNet.Runtime.Engine.GameEngine(graph, gameView, null, settings);

            try
            {
                await engine.StepAsync();
                Log.Information("Game ended, returning to title");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Engine error");
            }
        });

        // Navigate back to title after game ends
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            StatusText.Text = "Title screen";
            GameHost.NavigateTo("Title");
        });
    }
}
