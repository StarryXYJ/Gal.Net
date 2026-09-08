# Shared game test case

This data-only game is the shared smoke-test fixture for both official sample hosts.

- Headless: `powershell -File scripts/run-headless-sample.ps1`
- Avalonia: `powershell -File scripts/run-avalonia-sample.ps1`

Each script writes saves and progress to an ignored directory under `artifacts/sample-profiles`, so running either host does not change the fixture.
