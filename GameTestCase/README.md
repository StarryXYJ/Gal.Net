# Shared game test case

This data-only game is the shared smoke-test fixture for both official sample hosts.

`bg*` files are used as backgrounds and the `xy*` PNG files as portrait resources.
The opening scene intentionally also references the non-existent
`portraits/missing-fallback.png` as one portrait, so the Avalonia fallback rendering
remains covered. Layer coordinates are design-pixel values (the Avalonia sample hosts
the game at 1920×1080).

Story path: start → station dialogue → select downtown or harbor → one route dialogue.
The opening scene keeps the background at the origin, then renders portraits and the
intentional missing-resource fallback at increasing z values. It therefore verifies visible
occlusion, z-order and resource fallback, plus a typewriter wait with
`\d{...}` delays, `\n`, `<b>`, `<i>`, and `<color>` rich text, choice input, transitions,
effects, variables and the selected route.

- Headless: `powershell -File scripts/run-headless-sample.ps1`
- Avalonia: `powershell -File scripts/run-avalonia-sample.ps1`

Each script writes saves and progress to an ignored directory under `artifacts/sample-profiles`, so running either host does not change the fixture.
