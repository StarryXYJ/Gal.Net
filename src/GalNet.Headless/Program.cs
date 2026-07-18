using System.Globalization;
using DynamicLocalization.Core;
using DynamicLocalization.Core.Providers;
using GalNet.Core.Settings;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Loader;
using GalNet.Runtime.SaveLoad;
using GalNet.Headless;

Console.WriteLine("GalNet Headless v0.2.0");
Console.WriteLine("Core Runtime MVP\n");

// ── I18n: DynamicLocalization CultureService ──
var i18n = new CultureService();
JsonLocalizationProvider provider = new();
provider.LoadJsonString("""
                        {
                          "Game": {
                            "Speaker": {
                              "speaker_narrator": "旁白",
                              "speaker_alice": "Alice"
                            },
                            "Node": {
                              "group_intro": {
                                "entry_1": { "Text": "欢迎来到这个世界...\\d{2000}黑暗中，你缓缓睁开眼睛。" },
                                "entry_4": { "Text": "你好，旅行者。我需要你的帮助。" }
                              },
                              "group_accept": {
                                "entry_1": { "Text": "谢谢你！请去东边的森林寻找魔法水晶。" }
                              },
                              "group_reject": {
                                "entry_1": { "Text": "没关系，也许下次吧..." }
                              },
                              "branch_choice": {
                                "option_0": { "Label": "接受任务" },
                                "option_1": { "Label": "拒绝" }
                              }
                            }
                          }
                        }
                        """, "zh-CN");
i18n.RegisterProvider(provider);
i18n.SetCulture("zh-CN");

Console.WriteLine($"[I18n] Culture: {i18n.CurrentCulture.Name}, languages: {string.Join(", ", i18n.AvailableCultures.Select(c => c.Name))}\n");

// ── 创建示例图 ──
var sampleDir = Path.Combine(AppContext.BaseDirectory, "sample");
Directory.CreateDirectory(sampleDir);

var graphJson = """
{
  "version": 1,
  "name": "DemoScene",
  "rootNodeId": "group_intro",
  "nodes": [
    {
      "id": "group_intro",
      "type": "Group",
      "name": "intro",
      "x": 100, "y": 220
    },
    {
      "id": "branch_choice",
      "type": "Branch",
      "name": "choice",
      "x": 350, "y": 220,
      "branchType": "Choice",
      "options": [
        { "text": "Game.Node.branch_choice.option_0.Label", "condition": "" },
        { "text": "Game.Node.branch_choice.option_1.Label", "condition": "" }
      ]
    },
    {
      "id": "group_accept",
      "type": "Group",
      "name": "accept",
      "x": 600, "y": 120
    },
    {
      "id": "group_reject",
      "type": "Group",
      "name": "reject",
      "x": 600, "y": 320
    }
  ],
  "edges": [
    { "fromNodeId": "group_intro", "fromOutlet": 0, "toNodeId": "branch_choice" },
    { "fromNodeId": "branch_choice", "fromOutlet": 0, "toNodeId": "group_accept" },
    { "fromNodeId": "branch_choice", "fromOutlet": 1, "toNodeId": "group_reject" }
  ]
}
""";
var graphPath = Path.Combine(sampleDir, "graph.json");
File.WriteAllText(graphPath, graphJson);

// ── 创建 .galgroup 文件 ──
// 条目类型使用英文标识，speaker/content 使用 i18n key 路径
File.WriteAllText(Path.Combine(sampleDir, "intro.galgroup"), """
    text : speaker:Game.Speaker.speaker_narrator; content:Game.Node.group_intro.entry_1.Text
    wait : duration:0.5
    layer.show : id:bg; asset:bg_classroom
    text : speaker:Game.Speaker.speaker_alice; content:Game.Node.group_intro.entry_4.Text
    variable.set : target:met_alice; value:true; valueType:bool
    """);

File.WriteAllText(Path.Combine(sampleDir, "accept.galgroup"), """
    text : speaker:Game.Speaker.speaker_alice; content:Game.Node.group_accept.entry_1.Text
    variable.set : target:quest_accepted; value:true; valueType:bool
    """);

File.WriteAllText(Path.Combine(sampleDir, "reject.galgroup"), """
    text : speaker:Game.Speaker.speaker_alice; content:Game.Node.group_reject.entry_1.Text
    """);

// ── 加载图 ──
Console.WriteLine("[Loader] Loading graph...");
var graph = GraphLoader.LoadFromFile(graphPath);

var introGroup = graph.Nodes.OfType<GalNet.Core.Graph.Group>().First(n => n.Id == "group_intro");
var acceptGroup = graph.Nodes.OfType<GalNet.Core.Graph.Group>().First(n => n.Id == "group_accept");
var rejectGroup = graph.Nodes.OfType<GalNet.Core.Graph.Group>().First(n => n.Id == "group_reject");

GalgroupLoader.LoadIntoGroupFromContent(introGroup, File.ReadAllText(Path.Combine(sampleDir, "intro.galgroup")));
GalgroupLoader.LoadIntoGroupFromContent(acceptGroup, File.ReadAllText(Path.Combine(sampleDir, "accept.galgroup")));
GalgroupLoader.LoadIntoGroupFromContent(rejectGroup, File.ReadAllText(Path.Combine(sampleDir, "reject.galgroup")));

Console.WriteLine($"[Loader] Graph '{graph.Name}' loaded: {graph.Nodes.Count} nodes, {graph.Edges.Count} edges");
Console.WriteLine($"[Loader] Compiled entries: intro={introGroup.Entries.Count}, accept={acceptGroup.Entries.Count}, reject={rejectGroup.Entries.Count}\n");

// ── 运行引擎 ──
Console.WriteLine("[Engine] Starting game loop...\n");

var gameSettings = new GameSettings { TextSpeed = 20f };
var settings = new SettingsContainer();
settings.Set(gameSettings);

var view = new HeadlessGameView(verbose: true, gameSettings);
var engine = new GameEngine(graph, view, i18n, settings);

var finished = await engine.StepAsync();

Console.WriteLine($"\n[Engine] Game finished: {finished}");

// ── 测试存档 ──
Console.WriteLine("\n[Save] Testing save/load...");
var saveData = engine.CreateSaveData();
var saveJson = SaveManager.Serialize(saveData);
Console.WriteLine($"[Save] Serialized size: {saveJson.Length} bytes");

var restored = SaveManager.Deserialize(saveJson);
Console.WriteLine($"[Save] Restored: nodeId={restored?.NodeId}, entryIndex={restored?.EntryIndex}");

Console.WriteLine("\nPhase 1 MVP complete.");
