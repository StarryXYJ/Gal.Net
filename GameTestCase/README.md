# Entry instance runtime smoke test

This data-only game exercises the currently implemented runtime slice:

- `layer.show`, `layer.replace`, `layer.move`, and `layer.hide` through `ILayerPresenter`;
- blocking `dialogue.text` with typewriter directives and `\skip` boundaries;
- Engine-owned Choice filtering/index mapping, condition evaluation, and node jumps;
- GUID-backed audio, effect-program, and particle resource references.

The `.galgroup` files use the version 2 compiled format (`typeId` plus `arguments`). Every resource
reference is its stable GUID; human-readable paths occur only in adjacent `.meta` files. The tiled
route uses ColorGrade, and the filled route uses the Snow sprite for its particle definition.

Run directly from the repository root without a PowerShell script:

```text
dotnet run --project src/Samples/GalNet.Sample.Headless/GalNet.Sample.Headless.csproj -- GameTestCase
```
