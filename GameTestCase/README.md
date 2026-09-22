# Entry instance runtime smoke test

This data-only game exercises the currently implemented runtime slice:

- `layer.show`, `layer.replace`, `layer.move`, and `layer.hide` through `ILayerPresenter`;
- blocking `dialogue.text` with typewriter directives and `\skip` boundaries;
- Engine-owned Choice filtering/index mapping, condition evaluation, and node jumps;
- safe continuation through registered but not-yet-implemented primitive modules.

The `.galgroup` files use the version 2 compiled format (`typeId` plus `arguments`).
Both routes reuse the existing image assets. Audio, effect, and particle entries are deliberately
present to demonstrate that their current immediate-completion instances do not block the story.

Run directly from the repository root without a PowerShell script:

```text
dotnet run --project src/Samples/GalNet.Sample.Headless/GalNet.Sample.Headless.csproj -- GameTestCase
```
