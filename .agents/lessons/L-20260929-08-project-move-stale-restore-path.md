# Project moves must discard stale build intermediates before restore

Status: confirmed
Source feature: F-20260929-13-repository-physical-layout

## Symptom

After moving tracked project directories, the first `dotnet test` restore recreated `src/GalNet.Core` and `src/GalNet.Primitives.Builtins` at their old locations. Each recreated directory contained `obj` and `packages.lock.json`, making the staged move look like a duplicate lock file instead of a clean rename.

## Root cause

The moved project directories still contained ignored `obj` data generated at the old absolute path. NuGet/MSBuild reused that intermediate restore metadata and wrote lock/intermediate files back to the recorded old project location.

## Correct approach

After moving a .NET project directory, remove that project's moved `bin` and `obj` directories before the first restore. Then run `dotnet restore --locked-mode` from the new path and verify that no old project directory was recreated before staging the move.

## Scope

Physical project-directory moves in GalNet, especially when `packages.lock.json` is tracked and existing local `obj` directories move with the source tree.

## Do not

Do not assess rename completeness or stage generated old-path lock files until moved build intermediates have been discarded and restore has been repeated from the new location.

## Evidence

Removing the moved intermediates and the two recreated old directories, then running locked restore outside the sandbox, restored all projects under `src/Shared` and did not recreate either old path.
