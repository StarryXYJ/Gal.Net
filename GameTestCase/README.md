# Compiled-content runtime and presentation smoke test

This Raw-authoring game exercises the compiled runtime slice:

- `layer.show`, `layer.replace`, `layer.move`, and `layer.hide` through `ILayerPresenter`;
- blocking `dialogue.text` with typewriter directives and `\skip` boundaries;
- Engine-owned Choice filtering/index mapping, condition evaluation, and node jumps;
- a composite `transition.crossFade` compiled into an animation plan;
- authored layer and particle-property animations;
- Runtime-backed particle start/stop with GUID-backed texture resources.

The `.rawgalgroup` files are the sole authoring source. Build them with `GalNet.Editor.Headless`;
the generated `.galgroup` files use the version 2 compiled format (`typeId` plus `arguments`). Every
resource reference is its stable GUID; human-readable paths occur only in adjacent `.meta` files.

The fixture follows the editable project layout: `Graph/graph.json`, `Graph/groups/*.galgroup`, and
`Assets/` with each resource's `.meta`. Gallery data is derived from the metadata rather than stored
as an editable root `gallery.json`. The classroom background is also a `cg` Gallery item (ID `100`),
so a host that supplies the standard Gallery pages shows the Gallery button on its title page. The
intro group executes `gallery.unlock(100)`, making that item viewable after the game starts.

Build and run from the repository root:

```text
dotnet run --project src/GalNet.Editor.Headless/GalNet.Editor.Headless.csproj -- build GameTestCase --output artifacts/sample-build
dotnet run --project src/Samples/GalNet.Sample.Headless/GalNet.Sample.Headless.csproj -- artifacts/sample-build
```
