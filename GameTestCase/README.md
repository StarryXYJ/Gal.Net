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
the generated `.galgroup` files use the version 2 compiled format (`typeId` plus `arguments`). Every
resource reference is its stable GUID; human-readable paths occur only in adjacent `.meta` files.

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
