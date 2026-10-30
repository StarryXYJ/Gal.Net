# Desktop application publish matrix

`publish-applications.py` builds the real Editor, embedded CLI, and CrossPlatform Player/Developer with the exact SDK in `global.json` (four applications by default). It uses the separate `Desktop/<rid>` restore graph, locked restore followed by `publish --no-restore`, and the ordinary self-contained profile (no AOT, trimming, single file or R2R). The CrossPlatform host must carry the pinned SDL native payload and notices. Android has a separate Mono AOT publish gate.

```sh
python scripts/ci/publish-applications.py --target linux-x64 --output out/application-publish
```

Python 3.11+, the pinned SDK, CMake and a native C compiler are required. The default builds miniaudio on the matching native host. Windows can supply the library built by the existing project target:

```powershell
python scripts/ci/publish-applications.py --target win-x64 --output out/application-publish --native-audio src/Infrastructure/GalNet.Media.Miniaudio/obj/native/win-x64/galnet_miniaudio.dll
```

For cross-host publishing, explicitly supply `--native-audio` for the target architecture. `--dotnet` selects an isolated SDK executable; `--repository` supports a clean checkout/snapshot. `--flavors Player` restricts CrossPlatform to Player; both variants are checked by default. A maintainer can explicitly use `--update-locks` when creating a new RID graph. CI never uses that option.

Use an empty output directory. Each SDK/native step has a separate log and exit code. `application-publish.json` records every published file/hash, native audio provenance and SDK version. Validation checks target PE/ELF/Mach-O headers, the self-contained runtime, required contract closure, private SDL SHA256/notices, and identical shared Core/Builtins bytes **within that host's publish set**. CLI uses its root Core/Builtins; the full DefaultContracts closure is supplied by the assembled Editor package or by an explicit consumer contract directory.

CrossPlatform explicitly compiles debug/launch capabilities for its selected flavor. Player outputs reject optional debugging/protocol/Developer Tools dependencies; Developer outputs require them. Neither flavor distributes PDBs: external session debugging is protocol-based rather than source-level stepping. The report records flavor/capabilities, and both flavors must use identical project contract bytes.

`--flavors Developer Player Bound --bind-game` additionally exports GameTestCase using the actual Player contract closure and compiles a Bound host with the inspected project identity. Developer enables debugging and external launch arguments; Player disables debugging but permits external content; Bound disables both and rejects another project's content. `GalNetPlayerMode` is shared with NuGet consumers; see the [release manual](../../docs/manual/publishing/engine-release.md). `--deployment FrameworkDependent` produces the runtime-dependent variants and rejects an accidentally copied CoreCLR; the default remains SelfContained. `--package-apphosts` also creates SDK apphosts targeting `lib/` for final packaging.

These are raw SDK outputs and compile/deployment gates. They are not final installable packages: private video closure, `.app` construction/signing, game content, actual GUI/device/media runtime and minimum OS acceptance have separate checks. The report explicitly records those runtime gaps. Builds from different hosts/source contexts are not asserted to produce interchangeable contract bytes; strict composition continues to require the exact contract artifacts used by the content export.

The `Application portability` workflow watches presentation/host/backend/shared/Editor/CrossPlatform changes and the build configuration. It publishes on Windows x64 and Ubuntu x64 runners, then separately publishes the Android ARM64 GameTestCase APK with Mono AOT. A Windows job also creates temporary `0.0.0-dev.1` NuGet packages and compiles/runs out-of-repository consumers, including the optional debugging package. All jobs are preview compile/deployment gates, not runtime acceptance; Android uses CI/debug signing and is not a store-ready artifact. Local Windows, Linux and device evidence is recorded in the cross-platform feature.

The formal Release workflow now builds the three modes on both desktop platforms in both deployments, plus a bound non-debug Android APK. `scripts/release/cross-platform-release.py` assembles and verifies the archives, and seals all 19 application/contract assets before Push can publish. Private Linux media preparation is shared between deployments. The original Windows Avalonia Sample is not part of that public matrix.
