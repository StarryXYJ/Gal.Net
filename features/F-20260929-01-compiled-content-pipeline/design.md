---
feature: F-20260929-01-compiled-content-pipeline
status: proposed
created: 2026-09-29
updated: 2026-09-29
---

# 编译内容管线与可运行示例设计

## 结论

保留现有的硬边界：`.rawgalgroup` 是 Editor authoring source，`.galgroup` 是 Runtime-only primitive envelope。新增 `ProjectContentBuilder`，由 Headless CLI、导出器和 Sample 脚本共用；它在独立 staging 目录构造可运行目录，然后原子替换目标。Runtime 不获得 Raw parser 或编译器依赖。

```text
Authoring project
  Graph/graph.json + Graph/**/*.rawgalgroup + Assets/I18n/settings
          │
          ▼
Editor.Shared ProjectContentBuilder
  validate graph-to-source mapping
  GalgroupCompiler (recommended target profile)
          │
          ├── Development output → Sample Headless / Avalonia
          └── export staging     → GamePackageExporter → .galpak
```

## 输入、输出与映射

`ProjectContentBuilder.BuildAsync(projectRoot, outputRoot)` reads the editable graph through the existing Editor document repository and uses each Group node's `file` as its Raw source location. Group source locations are normalized relative to `Graph/`, must end in `.rawgalgroup`, and must remain under that directory.

The builder recursively enumerates `Graph/**/*.rawgalgroup` and requires an exact one-to-one mapping with Group-node source files:

- a referenced source that is missing fails;
- two Group nodes referencing the same source fails;
- an enumerated Raw source not referenced by a Group fails;
- a Group without a file follows the Editor's canonical `groups/<id>.rawgalgroup` convention before validation.

Every source is compiled by `GalgroupCompiler` using `BuiltinEntryModules.CreateRecommendedTargetProfile()`. Regardless of authoring source nesting, output follows the existing Runtime convention `Graph/groups/<group-id>.galgroup`; this deliberately matches `GameGraphContentLoader` and avoids widening the Runtime graph model in this feature.

The generated directory includes only runtime inputs: `settings.json` (when present), `Assets/**`, `I18n/**`, generated `Graph/graph.json`, and generated `Graph/groups/*.galgroup`. It never includes Raw Groups, `.meta` filtering is not applied in directory builds because development asset loading requires metadata, and it never copies `Output/`, `Temp/`, `.galnet/`, or authoring-only editor files.

## Atomicity and safety

The builder constructs a sibling temporary directory and validates all source groups before replacing the requested output. It rejects a target equal to the project root, a target ancestor of the project, and a target within `Graph`, `Assets`, `I18n`, `.galnet`, or `Temp`. The default is `<project>/Output`; a failed compile leaves its prior output untouched. Replacement only deletes/replaces the resolved output target after staging succeeds.

Output graph JSON is copied from the editable graph. Its `file` authoring hints may still name Raw sources because current Runtime graph loading intentionally ignores this field and discovers compiled group files by Group ID. A future runtime graph-format revision can remove these editor hints from release output; it is not required for correct loading today.

## CLI and export

Headless gains:

```text
galnet-editor-headless build <project> [--output <directory>]
```

It prints JSON containing success, output path, compiled Group count and diagnostics. `export` calls the builder against a private staging directory and passes that generated directory to the package writer. The package writer no longer needs to know about Raw sources; its Graph collection is therefore runtime-only by construction. Staging cleanup occurs on success and failure.

## Sample behavior

Both sample PowerShell scripts accept `-Project` and `-BuildOutput`. Their defaults remain `GameTestCase` and an artifact-directory build output. Unless `-SkipBuild` is requested, each invokes Headless `build`, stops on a non-zero exit code, and passes the generated directory—not the authoring root—to its Sample executable. Profile/save arguments retain their existing semantics.

## Example content

`GameTestCase` becomes a Raw source fixture. Its route demonstrates a composite transition (compiled to an animation plan), an authored layer animation, and a complete particle emitter definition. Before using it, Builtins gains particle primitive instances that parse the definition, write `SceneState.ActiveParticleEmitters`, call `IParticlePresenter`, and remove both state and presenter on stop. The focus remains a short deterministic smoke game, not a production narrative.

## Compatibility and risks

- Existing checked-in compiled fixture files cease to be authoring input. The Runtime loader's rejection of Raw remains covered by tests.
- Building via the recommended target profile makes unavailable or invalid entry types fail before a player is launched, which is intended.
- Directory copying can be expensive for large assets; correctness and isolation come first. Incremental builds are explicitly deferred.
- Raw source filenames need not equal node IDs, but the output remains node-ID-based. This preserves the production loader without conflating authoring organization with runtime addressing.

## Alternatives rejected

1. **Teach Runtime to accept Raw Groups**: rejected because it gives Runtime Editor/compiler responsibilities, permits composite entries at execution time, and makes target-profile errors launch-time failures.
2. **Have scripts compile files themselves**: rejected because export, CLI, preview and scripts would drift across separate implementations.
3. **Copy Raw and Compiled Groups beside each other**: rejected because it leaks authoring source into runtime artifacts and makes the active input ambiguous.
