# Desktop application publish matrix

`publish-applications.py` builds the real Editor, CLI, Player and Developer applications with the exact SDK in `global.json`. It uses the separate `Desktop/<rid>` restore graph, locked restore followed by `publish --no-restore`, and the ordinary self-contained profile (no AOT, trimming, single file or R2R).

```sh
python scripts/ci/publish-applications.py --target linux-x64 --output out/application-publish
```

Python 3.11+, the pinned SDK, CMake and a native C compiler are required. The default builds miniaudio on the matching native host. Windows can supply the library built by the existing project target:

```powershell
python scripts/ci/publish-applications.py --target win-x64 --output out/application-publish --native-audio src/Infrastructure/GalNet.Media.Miniaudio/obj/native/win-x64/galnet_miniaudio.dll
```

For cross-host publishing, explicitly supply `--native-audio` for the target architecture. `--dotnet` selects an isolated SDK executable; `--repository` supports a clean checkout/snapshot. `--flavors Player` restricts the Sample variants; both variants are checked by default. A maintainer can explicitly use `--update-locks` when creating a new RID graph. CI never uses that option.

Use an empty output directory. Each SDK/native step has a separate log and exit code. `application-publish.json` records every published file/hash, native audio provenance and SDK version. Validation checks target PE/ELF/Mach-O headers, the self-contained runtime, required contract closure, private SDL SHA256/notices, and identical shared Core/Builtins bytes **within that host's publish set**. CLI uses its root Core/Builtins; the full DefaultContracts closure is supplied by the assembled Editor package or by an explicit consumer contract directory.

These are raw SDK outputs and compile/deployment gates. They are not final installable packages: private video closure, `.app` construction/signing, game content, actual GUI/device/media runtime and minimum OS acceptance have separate checks. The report explicitly records those runtime gaps. Builds from different hosts/source contexts are not asserted to produce interchangeable contract bytes; strict composition continues to require the exact contract artifacts used by the content export.

The `Application portability` workflow watches presentation/host/backend/shared/Editor/Sample changes and the build configuration. It publishes on Windows x64, Ubuntu x64, macOS Intel and macOS ARM runners, retaining reports/logs. macOS runner labels follow the [GitHub runner reference](https://docs.github.com/en/actions/reference/runners/github-hosted-runners). Configuration and restore success do not count as an executed macOS job. Local Windows and Linux SDK evidence is recorded in the cross-platform feature.
