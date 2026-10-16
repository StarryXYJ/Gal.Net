# Compiled-content runtime and presentation smoke test

This Raw-authoring game exercises the compiled runtime slice:

The game includes English (`en-US`, its configured default) and Simplified Chinese
(`zh-CN`). All raw dialogue/NVL content and choice text are localization keys.
`speaker` references the `guide` character in `I18n/catalog/characters.json`;
character names, choice text and each group's text live in separate per-culture JSON
files. Change the language in the Avalonia Sample settings to localize subsequent
dialogue, NVL paragraphs and choices using the same culture as the UI.

Third-party translations go in `<game-directory>/I18n/External/<culture>/**/*.json`.
They override built-in game and UI keys (for example `App.Title`); files are applied
in ordinal relative-path order, last file wins. Invalid files are logged and skipped.
New languages appear in settings. Restart to load edited files; currently displayed
dialogue and NVL history do not change when selecting a language. External files are
excluded from exported packages and preserved when reopening an existing installation.

On the Avalonia Sample's first successful launch for a profile, a language-selection
page appears before the title. Confirm to persist the choice and increment the startup
count; cancel exits without completing onboarding. Later launches restore the language
and increment the count. Debug **Clear game data** resets player variables and restarts,
so the picker reappears. Use `--profile <directory>` for an isolated test profile.

- `layer.show`, `layer.replace`, `layer.move`, and `layer.hide` through `ILayerPresenter`;
- `niko.mp4` as a real scene layer: blocking `video.play` with `[3, 6, 9, 999999]` second skip points, then transformed non-blocking playback and explicit `video.stop`; the portrait stays above it by z order;
- blocking `dialogue.text` with typewriter directives and `\skip` boundaries;
- explicit `nvl.show`, `nvl.append`, `nvl.clear`, and `nvl.hide`, including rich-text and typewriter NVL history;
- Engine-owned Choice filtering/index mapping, condition evaluation, and node jumps;
- a composite `transition.crossFade` compiled into an animation plan;
- authored layer and particle-property animations;
- Runtime-backed particle start/stop with GUID-backed texture resources.
- Configurable `bgm`, `sfx`, and `vocal` tracks: the intro uses `心.mp3` to demonstrate in-progress BGM fade-in, `seffect.gain` apply/stop, `seffect.lowPass` / `seffect.highPass` apply/stop, and in-progress fade-out.

The `.rawgalgroup` files are the sole authoring source. Build them with `GalNet.Editor.Headless`;
the generated `.galgroup` files use the version 3 compiled format (`typeId`, `arguments`, and reading metadata). The raw authoring format remains version 2. Every
resource reference is its stable GUID; human-readable paths occur only in adjacent `.meta` files.

Entry IDs must be unique within each group, not across groups. Read progress uses
the group ID together with the compiled entry ID. The current `progress.json`
format is version 2; older progress is rejected rather than migrated. For an old
local profile, move `progress.json` aside as a backup before launching again, or
use a new `--profile` directory. This resets read history only; retain player
variables and saves separately. Rebuild content after changing the compiler or
authoring source; do not use `-SkipBuild` with stale output.

The fixture follows the editable project layout: `Graph/graph.json`, `Graph/groups/*.rawgalgroup`, and
`Assets/` with each resource's `.meta`. Gallery data is derived from the metadata rather than stored
as an editable root `gallery.json`. The classroom background is also a `cg` Gallery item (ID `100`),
so a host that supplies the standard Gallery pages shows the Gallery button on its title page. The
intro group executes `gallery.unlock(100)`, making that item viewable after the game starts.

Build and run from the repository root:

```text
dotnet run --project src/Editor/GalNet.Editor.Headless/GalNet.Editor.Headless.csproj -- build GameTestCase --output artifacts/sample-build
dotnet run --project src/Samples/GalNet.Sample.Headless/GalNet.Sample.Headless.csproj -- artifacts/sample-build
dotnet run --project src/Samples/GalNet.Sample.Avalonia/GalNet.Sample.Avalonia.csproj -- artifacts/sample-build
```

Video smoke: advance the first dialogue to reach blocking playback. Each left click seeks to the next point after the current position; the oversized final point ends playback. The following non-blocking playback uses rotation, scale and opacity below the portrait, then stops on dialogue advance. Headless has no video presenter and completes these entries immediately.

Native decode tests are opt-in: set `GALNET_VIDEO_ASSETS` to the absolute `GameTestCase/Assets` directory and `GALNET_VIDEO_LIBVLC` to the native LibVLC directory, then run Presentation tests with `--filter FullyQualifiedName~VideoNativeSmokeTests`. `GALNET_VIDEO_CAPTURE` optionally exports a composed scene PNG. The tests check actual decoded pixels, sequential seeks, natural completion and cancellation of loading during hide/replace.

## 项目 Entry 契约

本目录是当前格式的作者内容，entry 集合由独立 Sample.Contracts 项目声明。先从仓库根构建并部署工具契约，再使用 CLI 或打开 Editor：

```powershell
./scripts/build-project-contracts.ps1 -ContractsProject ./src/Samples/GalNet.Sample.Contracts/GalNet.Sample.Contracts.csproj -ProjectDirectory ./GameTestCase
dotnet run --project ./src/Editor/GalNet.Editor.Headless -- schema list ./GameTestCase
dotnet run --project ./src/Editor/GalNet.Editor.Headless -- validate ./GameTestCase
```

Contracts/ 是本机构建产物，不提交 DLL；游戏 Sample 正常引用同一契约项目。缺 DLL 时工具不回退内置，原始查询和结构校验仍可用。参数和命令见 [CLI README](../src/Editor/GalNet.Editor.Headless/README.md)。

平台截图示例独立放在 [Sample.Contracts/Examples](../src/Samples/GalNet.Sample.Contracts/Examples/screenshot.rawgalgroup)，不自动加入此项目的默认剧情，所以 Headless 默认流程仍可执行。


## 模块状态存档 smoke

intro 在 gain 效果启用后新增暂停 BGM 的对白检查点。运行到提示时新建存档，然后读档：背景/立绘与 gain 轨道效果应恢复，BGM 保持暂停；推进执行 audio.resume 后从头播放，并保留该效果直到后续 stop 指令。使用新存档验证，旧格式文件不做迁移。

自动回归 `ModularSaveRestoreTests` 直接编译该实际作者文件中的图层/音频条目，通过 FileSaveService 写入全新槽位，再由 Engine 和注册式恢复会话读回；后端使用可观察的测试 host。实际 LibVLC 声音效果仍需按上面的原生媒体方式人工检查。
