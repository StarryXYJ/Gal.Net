# Shared game test case

This data-only game is the shared smoke-test fixture for both official sample hosts.

The fixture intentionally contains no media files. The Avalonia sample renders every
missing layer as a labelled square, so the scene/layer sequence remains visible while
the repository stays small. Layer coordinates are design-pixel values (the Avalonia
sample hosts the game at 1920×1080).

Story path: start → station dialogue → select downtown or harbor → one route dialogue.
The station scene therefore verifies background/character layers, a typewriter wait,
choice input, transitions, effects, variables and the selected route.

- Headless: `powershell -File scripts/run-headless-sample.ps1`
- Avalonia: `powershell -File scripts/run-avalonia-sample.ps1`

Each script writes saves and progress to an ignored directory under `artifacts/sample-profiles`, so running either host does not change the fixture.
