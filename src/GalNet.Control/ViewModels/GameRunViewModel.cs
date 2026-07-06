using System;
using System.IO;
using System.Threading.Tasks;
using GalNet.Control.View;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Runtime.Session;
using Serilog;

namespace GalNet.Control.ViewModels;

/// <summary>
/// 游戏运行页 ViewModel。
/// 持有 DefaultGameView 供 UI 渲染，委托 DefaultGameSession 运行游戏到结束。
/// </summary>
public class GameRunViewModel
{
    public DefaultGameView GameView { get; }

    public GameRunViewModel(DefaultGameView gameView, ISettingsService settingsService, Action? onGameEnded = null)
    {
        GameView = gameView;
        _settingsService = settingsService;
        _onGameEnded = onGameEnded;
        
        var sampleDir = Path.Combine(AppContext.BaseDirectory, "sample");
        EnsureSampleData(sampleDir);

        var settings = new SettingsContainer();
        settings.Set(_settingsService.GetSnapshot());

        // 委托给默认游戏会话运行
        var session = new DefaultGameSession(GameView, settings, sampleDir);
        _ = session.RunAsync(() =>
        {
            if (_onGameEnded != null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(_onGameEnded);
            }
        });
    }

    private readonly ISettingsService _settingsService;
    private readonly Action? _onGameEnded;

    private static void EnsureSampleData(string sampleDir)
    {
        try
        {
            if (Directory.Exists(sampleDir) && File.Exists(Path.Combine(sampleDir, "graph.json")))
                return;

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
                "text : speaker:; content:Hello from GalNet\\d{1000}Let's make a choice.");

            File.WriteAllText(Path.Combine(sampleDir, "left.galgroup"),
                "text : content:You chose the left path.");

            File.WriteAllText(Path.Combine(sampleDir, "right.galgroup"),
                "text : content:You chose the right path.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to write default sample data.");
        }
    }
}
