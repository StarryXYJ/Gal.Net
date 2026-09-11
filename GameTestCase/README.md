# Shared game test case

This data-only game is the shared smoke-test fixture for both official sample hosts. Its
`.galgroup` files are compiled Runtime content; editor source uses the `.rawgalgroup` suffix.

`bg*` files are used as backgrounds and the `xy*` PNG files as portrait resources.
The opening scene intentionally also references the non-existent
`portraits/missing-fallback.png` as one portrait, so the Avalonia fallback rendering
remains covered. Layer coordinates are design-pixel values (the Avalonia sample hosts
the game at 1920×1080).

Story path: start → station dialogue → choose a tiled or filled background → route dialogue.
The opening scene contains only one filled background, one normal portrait and one
intentional missing-resource fallback portrait. It therefore verifies visible occlusion,
z-order and resource fallback, plus a typewriter wait with
`\d{...}` delays, `\n`, `<b>`, `<i>`, and `<color>` rich text, choice input, transitions,
effects, variables and the selected route.

Before the first choice, the normal portrait runs one blocking, skippable keyframe
`animation.play` clip. Its parallel tracks cover `Step`, `Linear`, and `CubicHermite`
interpolation. It then starts a non-blocking looping rotation plus faster additive PingPong position
and opacity loops on the same portrait; the relative loops are composed over the ordinary rotation
without replacing it. After five dialogue lines, `animation.stop` with `AfterIteration` lets all
active loops complete their return to the base pose. The second choice runs a non-blocking,
skippable 48-frame cross-fade Plan:
it shows the Filled background at opacity 0, fades the two backgrounds together, then hides
the old one. This keeps the shared fixture useful for validating both timeline events and
property tracks in the headless and Avalonia hosts.

The first choice starts a non-blocking, skippable 54-frame black-field fade: a transient
pure-black Overlay covers the opening background, swaps to tiled `bg.png` at full black,
then fades away. The second choice keeps the comparison background in `Fill` mode and
uses the cross-fade Plan.

- Headless: `powershell -File scripts/run-headless-sample.ps1`
- Avalonia: `powershell -File scripts/run-avalonia-sample.ps1`

Each script writes saves and progress to an ignored directory under `artifacts/sample-profiles`, so running either host does not change the fixture.
