using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GalNet.Control.View;
using GalNet.Core.Graph;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Loader;
using Serilog;

namespace GalNet.Control.ViewModels;

/// <summary>
/// 游戏运行页 ViewModel。
/// 持有 DefaultGameView 供 UI 渲染，构造后自动启动引擎循环。
/// 通过构造函数拿到所有所需依赖，独立可运行。
/// </summary>
public class GameRunViewModel
{
    public DefaultGameView GameView { get; }

    public GameRunViewModel(DefaultGameView gameView, ISettingsService settingsService, Action? onGameEnded = null)
    {
        GameView = gameView;
        _settingsService = settingsService;
        _onGameEnded = onGameEnded;
        _ = RunGameAsync();
    }

    private readonly ISettingsService _settingsService;
    private readonly Action? _onGameEnded;

    private async Task RunGameAsync()
    {
        await Task.Run(async () =>
        {
            try
            {
                Log.Information("GameRunViewModel: writing sample data and starting engine");

                var sampleDir = Path.Combine(AppContext.BaseDirectory, "sample");
                Directory.CreateDirectory(sampleDir);

                WriteSampleData(sampleDir);

                var graph = GraphLoader.LoadFromFile(Path.Combine(sampleDir, "graph.json"));

                foreach (var group in graph.Nodes.OfType<Group>())
                {
                    var path = Path.Combine(sampleDir, $"{group.Id}.galgroup");
                    if (File.Exists(path))
                        GalgroupLoader.LoadIntoGroupFromContent(group, File.ReadAllText(path));
                }

                var settings = new SettingsContainer();
                settings.Set(_settingsService.GetSnapshot());

                var engine = new GameEngine(graph, GameView, null, settings);

                try
                {
                    await engine.StepAsync();
                    Log.Information("Game ended");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Engine error");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GameRunViewModel: engine crashed");
            }

            // 游戏结束 → 回调通知主机返回开始页
            if (_onGameEnded != null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _onGameEnded());
            }
        });
    }

    private static void WriteSampleData(string sampleDir)
    {
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
}
