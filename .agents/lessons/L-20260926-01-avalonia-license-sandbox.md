# Avalonia build can enumerate a user-local licensing directory outside the workspace

Status: candidate  
Source feature: F-20260926-01-gallery-meta-pages

## Symptom

Building `test/GeneralTest/GeneralTest.csproj` with `AVALONIA_TELEMETRY_OPTOUT=1` can still fail in `AvaloniaStatsTask` with `UnauthorizedAccessException` for `C:\Users\Starry\AppData\Local\AvaloniaUI\Licensing\Tickets\v2`.

## Root cause

Avalonia BuildServices resolves its licensing/accelerate tier by enumerating that user-local ticket directory. The directory is outside the restricted workspace read scope. Telemetry opt-out does not avoid this licensing lookup.

## Correct approach

Treat this as an environment limitation, not a Gallery or UI compilation regression. Build affected non-Avalonia projects directly, and validate Avalonia projects only when the environment can read the licensing directory. Do not alter application code, package versions, or build targets to suppress the lookup.

## Scope

Any local GalNet build that compiles an Avalonia project under the restricted desktop sandbox.
