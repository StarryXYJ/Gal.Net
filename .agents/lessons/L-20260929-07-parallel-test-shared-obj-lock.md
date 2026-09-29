# Test projects sharing production dependencies must not build in parallel processes

Status: confirmed  
Source feature: F-20260929-11-game-host-presentation-split

## Symptom

Running the ten test projects through separate concurrent `dotnet test` processes caused intermittent `CS2012` failures while writing `src/GalNet.Core/obj/Release/net10.0/GalNet.Core.dll`. Some projects passed and others failed before tests started.

## Root cause

The test projects reference the same production projects and therefore write the same intermediate `obj/<Configuration>` outputs. Independent MSBuild processes do not coordinate those writes, even when each process uses `-m:1`.

## Correct approach

Run the repository test projects sequentially, or invoke them through one MSBuild graph that owns scheduling and shared project builds. Treat `CS2012` on a shared `obj` output during concurrent test commands as build-process contention, then rerun the complete matrix sequentially before assessing test health.

## Scope

GalNet repository-wide local test validation where multiple test projects share Core, Runtime, Presentation, Editor, or Storage project references.

## Do not

Do not launch one independent `dotnet test` process per project in parallel against the same configuration and workspace.

## Evidence

The parallel Release run failed on the shared `GalNet.Core.dll` intermediate output. The immediate sequential rerun completed all ten projects with 324/324 tests passing.
