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
            GameView.Cleanup();
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
            Directory.CreateDirectory(sampleDir);

            // Remove obsolete script files from previous runs so old nodes don't linger
            foreach (var f in Directory.EnumerateFiles(sampleDir, "*.galgroup"))
                File.Delete(f);
            if (File.Exists(Path.Combine(sampleDir, "graph.json")))
                File.Delete(Path.Combine(sampleDir, "graph.json"));

            // ── graph.json: 3 groups, linear flow ──
            File.WriteAllText(Path.Combine(sampleDir, "graph.json"), /*lang=json*/ """
            {
              "version": 1, "name": "DemoScene", "rootNodeId": "intro",
              "nodes": [
                { "id": "intro", "type": "Group", "name": "intro", "x": 100, "y": 100 },
                { "id": "showcase", "type": "Group", "name": "showcase", "x": 400, "y": 100 },
                { "id": "end", "type": "Group", "name": "end", "x": 700, "y": 100 }
              ],
              "edges": [
                { "fromNodeId": "intro", "fromOutlet": 0, "toNodeId": "showcase" },
                { "fromNodeId": "showcase", "fromOutlet": 0, "toNodeId": "end" }
              ]
            }
            """);

            // ── intro.galgroup: background + BGM + narrator + character appear + speak ──
            File.WriteAllText(Path.Combine(sampleDir, "intro.galgroup"), /*lang=galgroup*/ """
            layer : action:show; id:bg; asset:sample/bg_plains.jpg; x:0; y:0; z:0
            audio : action:play; channel:bgm; asset:sample/bgm_peaceful.mp3; volume:0.7; mode:loop
            text : speaker:Narrator; content:<b>Welcome to GalNet</b>\d{600}\nA world where stories come to life.\d{400}Where every choice shapes your path.\d{800}
            layer : action:show; id:chara_alice; asset:sample/chara_alice.png; x:120; y:180; z:10
            text : speaker:Alice; content:Hi there! I'm <b>Alice</b>.\d{400}\nI've been waiting for someone new to arrive!\d{800}\nCome on, let me show you around!
            text : speaker:Narrator; content:Alice smiles warmly, her eyes sparkling with excitement.\d{600}\nThe gentle breeze carries the scent of wildflowers across the plains.
            text : speaker:Alice; content:This place is <i>amazing</i>!\d{500}\nThere's so much to see and do here.\d{600}\nLet's start with a little <b>demonstration</b>!
            """);

            // ── showcase.galgroup: effects + background transition + character speaks ──
            File.WriteAllText(Path.Combine(sampleDir, "showcase.galgroup"), /*lang=galgroup*/ """
            effect : action:apply; type:shake; duration:0.3
            text : speaker:Alice; content:<b>Whoa!</b> Did you feel that?\d{600}\nThat's a <i>shake effect</i>!\d{400}\nThe engine supports all kinds of cool visuals.
            effect : action:apply; type:flash; duration:0.5
            text : speaker:Alice; content:*snaps fingers*\d{500}See that <b>flash</b>?\d{400}\nRight on cue!\d{800}\nThe <i>GalNet engine</i> can do so much more...
            text : speaker:Narrator; content:The scene shifts as Alice waves her hand through the air.\d{400}\nThe world around you begins to <b>dissolve</b> into a new landscape.
            layer : action:hide; id:bg; transition:dissolve; duration:0.8
            layer : action:show; id:bg; asset:sample/bg_castle.jpg; x:0; y:0; z:0; transition:dissolve; duration:0.8
            audio : action:play; channel:bgm; asset:sample/bgm_tense.mp3; volume:0.6; mode:loop
            text : speaker:Alice; content:Welcome to the <b>Grand Castle</b>!\d{600}\nThis is where the <i>real adventure</i> begins.\d{400}\nAre you ready?
            text : speaker:Narrator; content:Towering spires reach toward the sky.\d{400}\nThe castle walls hum with ancient magic.\d{600}\nYour journey is just beginning.
            """);

            // ── end.galgroup: final dialogue → no outgoing edge → engine stops ──
            File.WriteAllText(Path.Combine(sampleDir, "end.galgroup"), /*lang=galgroup*/ """
            audio : action:stop; channel:bgm
            audio : action:play; channel:bgm; asset:sample/bgm_peaceful.mp3; volume:0.3; mode:once
            text : speaker:Narrator; content:And so,\d{300} our <i>hero's journey</i> begins...\d{1000}
            text : speaker:Alice; content:Come on,\d{200} let's go!\d{200} I'll show you <b>everything</b>!\d{600}
            text : speaker:Narrator; content:To <b>be continued</b>...\d{1500}
            text : speaker:Alice; content:<i>See you next time!</i>
            """);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to write default sample data.");
        }
    }
}
