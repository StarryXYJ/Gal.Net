# Entry instance runtime smoke test

This data-only game exercises the currently implemented runtime slice:

- `layer.show`, `layer.replace`, `layer.move`, and `layer.hide` through `ILayerPresenter`;
- blocking `dialogue.text` with typewriter directives and `\skip` boundaries;
- Engine-owned Choice filtering/index mapping, condition evaluation, and node jumps;
- GUID-backed audio, effect-program, and particle resource references.

The `.galgroup` files use the version 2 compiled format (`typeId` plus `arguments`). Every resource
reference is its stable GUID; human-readable paths occur only in adjacent `.meta` files. The tiled
route uses ColorGrade, and the filled route uses the Snow sprite for its particle definition.

The fixture follows the editable project layout: `Graph/graph.json`, `Graph/groups/*.galgroup`, and
`Assets/` with each resource's `.meta`. Gallery data is derived from the metadata rather than stored
as an editable root `gallery.json`. The classroom background is also a `cg` Gallery item (ID `100`),
so a host that supplies the standard Gallery pages shows the Gallery button on its title page.

Run directly from the repository root without a PowerShell script:

```text
dotnet run --project src/Samples/GalNet.Sample.Headless/GalNet.Sample.Headless.csproj -- GameTestCase
```
