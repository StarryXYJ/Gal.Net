# Editor-maintained runtime and presentation smoke test

## Open and maintain in Editor

`GameTestCase` is an editable project shared by Editor, the Headless CLI, and the
Sample players. From the repository root, deploy the matching Sample contract:

```powershell
./scripts/contracts/build-project-contracts.ps1 -ContractsProject ./src/Samples/GalNet.Sample.Contracts/GalNet.Sample.Contracts.csproj -ProjectDirectory ./GameTestCase
dotnet run --project ./src/Editor/GalNet.Editor.Headless -- validate ./GameTestCase
dotnet run --project ./src/Editor/GalNet.Editor
```

In Editor, open the `GameTestCase` directory, not `Output/`. The graph starts at
an explicit Entry connected to Text and variants (`intro`). The showcase follows themed
chapters, with both transition routes converging before the final comparisons.
Initial node positions are stored in `Graph/graph.json`.

| Order | Groups | Demonstrations |
| --- | --- | --- |
| 1 | `intro`, `presentation-library` | Image variants, dialogue, NVL, text card positions and replacement |
| 2 | `media` | Blocking video, skip points, video alongside dialogue |
| 3 | `audio` | Fade, gain, pause/save/resume, filters, room, six additional sound effects, automation and ducking |
| 4 | `shaders` | Scene-post shaders, ShaderToy, grading, depth/rack focus and additional visual effects |
| 5 | `masks` | Layer clipping, letterbox, slow feathered blink, scene-only and UI-inclusive coverage |
| 6 | `characters` | Motion, shake, speed lines, impact, idle, explicit emphasis, mood, heartbeat and parallax |
| 7 | `weather` | Five weather presets plus radial rain, with overlapping natural tails |
| 8 | `routes`, `downtown` / `harbor`, `transitions` | Choice/condition, crossfade/blinds and all remaining transitions |
| 9 | `ending` | Completion |

Each chapter introduces the comparison and explicitly prepares its stage. Short-lived
transitions, masks, variant changes and animations have a bilingual dialogue cue before
they run; advance when ready to watch. Transient motion examples include a label and a
short pause. Shader, particle and audio examples
keep their observation dialogue; advance to stop or compare the next effect. Weather rounds use independent handles: advancing stops the old emission and starts
the next round immediately; existing particles fade out and release naturally. Rain,
snow and petals originate just above the frame. The legacy standalone snow/spark block is superseded by the weather presets;
all other previous cases remain present, including both gallery replay groups. Shader examples
now run for either route. Rebuild content after updating; group moves change reading
identity, so existing GameTestCase saves and read progress are not continuation fixtures.

Edit the existing Graph and Raw Groups, resources, characters, and localization
through Editor and use Save All before building. Raw Groups remain the sole
story source. Preserve Group/Entry IDs, localization keys, and resource `.meta`
IDs when maintaining the fixture; importing a duplicate resource creates a new
ID. Editor saves may reformat author JSON.

`Contracts/`, `Output/`, and `.galnet/` are local generated state. Rebuild and
deploy contracts after changing their source, then reopen the project. Keep the
Sample contract rather than replacing it with Editor's default contract.

For game testing, build the Debug Sample and select its executable in Editor's
project launch settings. Editor builds to `Output/Debug` and uses a separate
`.galnet/debug-profile`; see [external debugging](../docs/manual/editor/preview-and-debugging.md).
The Sample launch scripts below continue to build and run the same author files.

The editable default shaders live in `Assets/Effects/Default/`; see
[the visual authoring manual](../docs/manual/editor/default-visuals.md). The eyelid
example opens over two seconds, enlarges its aperture and fades remaining corner
coverage smoothly. Its scene-only and UI-inclusive variants are adjacent.

This Raw-authoring game exercises the compiled runtime slice:

The game includes English (`en-US`, its configured default) and Simplified Chinese
(`zh-CN`). All raw dialogue/NVL content and choice text are localization keys.
`speaker` references the `guide` character in `I18n/catalog/characters.json`;
character names, choice text and each group's text live in separate per-culture JSON
files. Change the language in the Avalonia Sample settings to localize subsequent
dialogue, NVL paragraphs and choices using the same culture as the UI.

Third-party translations go in `<game-directory>/I18n/External/<culture>.json`
or the legacy `<game-directory>/I18n/External/<culture>/**/*.json` layout.
They override built-in game and UI keys (for example `App.Title`); files are applied
in ordinal relative-path order, last file wins. Invalid files are logged and skipped.
New languages appear in settings. Restart to load edited files; currently displayed
dialogue and NVL history do not change when selecting a language. External files are
excluded from exported packages and preserved when reopening an existing installation.

`I18n/External/ja-JP.json` and `ko-KR.json` are small external-translation examples:
they translate the welcome, the first variant steps, the guide and route choices.
Other story text falls back to the project's English default. Development builds copy
these files into their output. External files are deliberately excluded from galpak;
to try them in an installed game, copy them manually into `pak/I18n/` and restart.
The Editor can export one universal template for translators; fill its `targetLocale`
and `translation` fields, then run `GalNet.Editor.Headless localization convert
translation.template.json --output ja-JP.json`. The output filename must match its
target culture and must not already exist.

On the Avalonia Sample's first successful launch for a profile, a language-selection
page appears before the title. Confirm to persist the choice and increment the startup
count; cancel exits without completing onboarding. Later launches restore the language
and increment the count. Debug **Clear game data** resets player variables and restarts,
so the picker reappears. Use `--profile <directory>` for an isolated test profile.

- `layer.show`, `layer.replace`, `layer.move`, and `layer.hide` through `ILayerPresenter`;
- `variants.show` / `variants.select` for the XY portrait and three-dimensional classroom backgrounds (see the walkthrough below);
- `niko.mp4` as a real scene layer: blocking `video.play` with `[3, 6, 9, 999999]` second skip points, then transformed non-blocking playback and explicit `video.stop`; the portrait stays above it by z order;
- blocking `dialogue.text` with typewriter directives and `\skip` boundaries;
- explicit `nvl.show`, `nvl.append`, `nvl.clear`, and `nvl.hide`, including rich-text and typewriter NVL history;
- Engine-owned Choice filtering/index mapping, condition evaluation, and node jumps;
- a composite `transition.crossFade` compiled into an animation plan;
- authored layer and particle-property animations;
- Runtime-backed particle start/stop with GUID-backed texture resources.
- Configurable `bgm`, `sfx`, and `vocal` tracks: the audio chapter uses `心.mp3` to demonstrate in-progress BGM fade-in, `seffect.gain` apply/stop, `seffect.lowPass` / `seffect.highPass` apply/stop, and in-progress fade-out.

The `.rawgalgroup` files are the sole authoring source. Build them with `GalNet.Editor.Headless`;
the generated `.galgroup` files use the version 3 compiled format (`typeId`, `arguments`, and reading metadata). The raw authoring format remains version 2. Every
resource reference is its stable GUID; human-readable paths occur only in adjacent `.meta` files.

## Image variants walkthrough

Rebuild the project after pulling these changes; an existing `Output/` still contains the old compiled intro.
The default intro now pauses at these checkpoints, in both English and Chinese:

| Checkpoint entry | Request / action | Visible result |
| --- | --- | --- |
| `portrait` | `pose=xy`, `expression=shock` | `xy_shock` |
| `variants-exact` | select `expression=shy` | `xy_shy` (Exact) |
| `variants-reduced` | select `pose=solo`, retain `shy` | `solo` (Reduced); save/load here |
| `variants-remembered` | select `pose=xy` | `xy_shy` restored |
| `variants-default` | background `scene=classroom`, `mode=speech`, `focus=empty` | `classroom_idle_empty` (Default); save/load here |
| `variants-scene` | select `focus=teacher`, retain `speech` | `classroom_speech_teacher` (Exact) |
| `variants-reset` | hide and show the portrait again | `xy_idle` (default selection) |
| `intro-line-2` | select `expression=wink` during the existing jitter loop | `xy_wink`, same moving layer |

`Assets/Variants/xy.variants` points to `xy/`: the first filename segment is `pose`,
the second is `expression`. The five renamed `xy_*.png` files retain their pixels;
`solo.png` is a copy of `xy_wink.png` used to demonstrate a shorter filename and retained selection.
This is a matching fixture, not an additional illustration.

`Assets/Variants/classroom.variants` points to `classroom/`, with `scene`, `mode`, and
`focus` dimensions. The two original classroom JPEGs are now named
`classroom_idle_empty.jpeg` and `classroom_speech_teacher.jpeg`. Their resource IDs
and the classroom Gallery item remain unchanged, so existing route references work.
Each atlas directory contains only its own images; unrelated assets are not scanned.

The descriptor IDs are `a1000000000000000000000000000020` (portrait) and
`a1000000000000000000000000000021` (classroom). Use these image-variants resources
in `variants.show`; each image still has its own sprite ID. See
[the authoring guide](../docs/manual/editor/image-variants.md) for creation and editing.

From the repository root, validate and rebuild with the current project contracts:

```powershell
./scripts/contracts/build-project-contracts.ps1 -ContractsProject ./src/Samples/GalNet.Sample.Contracts/GalNet.Sample.Contracts.csproj -ProjectDirectory ./GameTestCase
dotnet run --project ./src/Editor/GalNet.Editor.Headless -- validate ./GameTestCase
dotnet run --project ./src/Editor/GalNet.Editor.Headless -- build ./GameTestCase
```

When supplying a custom host, register the image-variants decoder and prepare its catalog
before constructing execution modules; pass that catalog to `AddBuiltins(imageVariants: ...)`.
The default Avalonia and Headless Samples already prepare it.

The shader chapter compares Bloom, two-pass Gaussian Blur and three adapted
ShaderToy programs on both routes. It then compares grading, Z-based depth and the
additional library shaders. ShaderToy examples use a plain stage, and animated
shader time is stopped before the effect is removed. Each demonstration ends before
the next begins; Blur's horizontal and vertical passes form one demonstration.

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

The Sample launch scripts build and deploy Sample.Contracts before compiling this
fixture, including on a fresh checkout:

```powershell
./scripts/samples/run-cross-platform-sample.ps1
./scripts/samples/run-headless-sample.ps1
```

`-SkipBuild` skips both contract deployment and content compilation. For direct
CLI use, deploy the contracts as shown above before running:

```text
dotnet run --project src/Editor/GalNet.Editor.Headless/GalNet.Editor.Headless.csproj -- build GameTestCase --output artifacts/sample-build
dotnet run --project src/Samples/GalNet.Sample.Headless/GalNet.Sample.Headless.csproj -- artifacts/sample-build
dotnet run --project src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Desktop/GalNet.Sample.CrossPlatform.Desktop.csproj -- artifacts/sample-build
```

Video smoke: advance the first dialogue to reach blocking playback. Each left click seeks to the next point after the current position; the oversized final point ends playback. The following non-blocking playback uses rotation, scale and opacity below the portrait, then stops on dialogue advance. Headless has no video presenter and completes these entries immediately.

Native decode tests are opt-in: set `GALNET_VIDEO_ASSETS` to the absolute `GameTestCase/Assets` directory and `GALNET_VIDEO_LIBVLC` to the native LibVLC directory, then run Presentation tests with `--filter FullyQualifiedName~VideoNativeSmokeTests`. `GALNET_VIDEO_CAPTURE` optionally exports a composed scene PNG. The tests check actual decoded pixels, sequential seeks, natural completion and cancellation of loading during hide/replace.

## 项目 Entry 契约

本目录是当前格式的作者内容，entry 集合由独立 Sample.Contracts 项目声明。先从仓库根构建并部署工具契约，再使用 CLI 或打开 Editor：

```powershell
./scripts/contracts/build-project-contracts.ps1 -ContractsProject ./src/Samples/GalNet.Sample.Contracts/GalNet.Sample.Contracts.csproj -ProjectDirectory ./GameTestCase
dotnet run --project ./src/Editor/GalNet.Editor.Headless -- schema list ./GameTestCase
dotnet run --project ./src/Editor/GalNet.Editor.Headless -- validate ./GameTestCase
```

Contracts/ 是本机构建产物，不提交 DLL；游戏 Sample 正常引用同一契约项目。缺 DLL 时工具不回退内置，原始查询和结构校验仍可用。参数和命令见 [CLI README](../src/Editor/GalNet.Editor.Headless/README.md)。

平台截图示例独立放在 [Sample.Contracts/Examples](../src/Samples/GalNet.Sample.Contracts/Examples/screenshot.rawgalgroup)，不自动加入此项目的默认剧情，所以 Headless 默认流程仍可执行。


## 模块状态存档 smoke

intro 在 gain 效果启用后新增暂停 BGM 的对白检查点。运行到提示时新建存档，然后读档：背景/立绘与 gain 轨道效果应恢复，BGM 保持暂停；推进执行 audio.resume 后从头播放，并保留该效果直到后续 stop 指令。使用新存档验证，旧格式文件不做迁移。

自动回归 `ModularSaveRestoreTests` 直接编译该实际作者文件中的图层/音频条目，通过 FileSaveService 写入全新槽位，再由 Engine 和注册式恢复会话读回；后端使用可观察的测试 host。实际 LibVLC 声音效果仍需按上面的原生媒体方式人工检查。

Audio uses project section v2 (`audioBuses` / `busId`). The intro combines real track low/high-pass filters with bus `seffect.reverb`; effect entries use `scope` / `targetId`. The default Windows x64 host uses miniaudio; videos remain on LibVLC.

## Gallery and resource-name coverage

The sample contains exactly two items in each built-in Gallery: images 100/101, music 200/201, videos 300/301, and scenes 400/401. The intro unlocks these eight items. Resource names are authored in Chinese and English in `I18n/<culture>/resources.json`; the long image title exercises ellipsis and tooltips.

Scene Groups `gallery-classroom` and `gallery-evening` have their own backgrounds and two dialogue lines. They are intentionally independent of the main flow and include a local unlock operation to exercise replay isolation. Their optional covers reference existing image resources. `Assets/Gallery/niko-preview.mp4` is a three-second remux of the existing sample video, used to verify switching video items without duplicating the full file.

This revision uses common `displayNameKey` / `galleryUnlockId` metadata and the audio `announceName` switch. Refresh the Sample contract DLLs and rebuild the project; the previous Gallery annotation and Entry display-name formats are not maintained.
