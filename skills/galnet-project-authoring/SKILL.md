---
name: galnet-project-authoring
description: Use the GalNet project CLI to turn scripts into Raw Groups, add staging to existing dialogue, and import character images or expression variants. Applies to authoring game projects; framework development uses the repository's development workflow.
---

# GalNet project authoring

Use the user's installed GalNet CLI and the target project's own contract. The editor can stay open: clean projects reload external changes automatically; unsaved editor content pauses reload and remains protected.

## Find the tool and project

- Prefer the CLI command/path supplied by the user, `GALNET_CLI`, or `GalNet.Editor.Headless` on PATH. A distributed CLI's `Skills/galnet-project-authoring` folder sits two levels below its executable. A `.dll` launch uses `dotnet <dll>`; source checkouts can build `src/Editor/GalNet.Editor.Headless` once and use its output.
- Require an explicit target project directory; the framework repository and a game project are different roots.
- Run `help --json` and query relevant operations with `help <operation-id> --json`. Read `inputSchema`, `documentInputSchema`, capability limits, structured diagnostics and exit codes. Avoid guessing operation names or parameter encodings.

## Read enough context

Use `project info`, Graph/Group queries, `schema list/show`, and resource/reference queries. Query the schemas for the entry types actually used by this project. Resource fields use sidecar IDs. `LocalizedText` stores text keys; `CharacterReference` stores catalog character IDs. Their visible text is stored separately.

Keep existing stable Group/Entry/node IDs, localization keys, unrelated author fields and entry order. For a performance-only request, retain dialogue and source text unless the user also requested rewriting. Do not assume a particular project's entry catalog includes the standard builtins.

## Prepare and apply changes

- Inspect Git status and existing edits. Preserve user changes. A branch or worktree is useful when the user's workflow allows it. Use Git diff to explain actual changes; commit, reset and rollback only within the user's authorization. A successful CLI operation has no persistent Undo; projects without Git need an existing backup for later rollback.
- Capture original target contents and SHA-256 before generating replacements. Generate new files under `Temp/` or outside the game project. Preserve that original hash in `expectedHash`; replacing it with a newer disk hash after a conflict could overwrite intervening user edits.
- Small structured modifications can use `execute` and its document batch dry run. Use `project apply` for native Graph/Raw Group/localization files and resources that must publish together. Discover its manifest with `help project.apply --json`.
- An apply manifest declares version 1, writes and deletions. Each write names a project-relative target, a source relative to the manifest directory, the source's SHA-256, and the target's original SHA-256. `expectedHash: null` explicitly means the target must not exist. Resources and `.meta` sidecars must exist or be deleted together; replacement metadata preserves the resource ID.
- Run `project apply <project> <manifest-file> --dry-run`, inspect diagnostics, and apply the same manifest when its effects match the authorized request. A stale target requires rereading and regenerating against the new content. The CLI's file transaction compensates failed publication; it does not replace Git history.
- Use the relevant Group/project validation and, when requested, compile/build. Respect reported skipped checks and analysis completeness. Report changed files, diagnostics and unresolved content decisions.

See [authoring recipes](references/authoring-recipes.md) for script conversion, staging existing dialogue and image/variant imports. Read the recipe relevant to the task.
