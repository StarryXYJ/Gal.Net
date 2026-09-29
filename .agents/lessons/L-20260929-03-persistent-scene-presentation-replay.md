# Persistent scene state requires an explicit presentation replay path

Status: confirmed  
Source feature: F-20260929-01-compiled-content-pipeline

## Symptom

Particle emitter definitions and animation values survived `GameRuntime.RestoreFrom`, but loading a save in the Avalonia Sample showed no particles. The Sample replayed `ActiveEffects` only, so `ActiveParticleEmitters` never reached `IParticlePresenter`.

## Root cause

Runtime snapshot restoration rebuilds platform-neutral state and Runtime scene instances; it deliberately does not recreate renderer-owned resources. Every persistent presentation object therefore also needs an explicit host replay step. Adding a new collection to `SceneState` is not sufficient by itself.

## How it was found

The particle primitive and snapshot paths were both present, while `SampleGameSessionService.RestorePersistentEffectsAsync` enumerated only `SceneState.ActiveEffects`. A replay test confirmed that rebuilding a particle request must preserve the emitter definition, z order, and saved animation values.

## Correct approach

Keep the save model platform-neutral, then use one shared Builtins replay helper to translate every persistent scene-object state into presenter requests after `GameRuntime.RestoreFrom`. Test the complete chain separately: primitive → Runtime state, snapshot → Runtime instance, and restored state → presenter request.

## Scope

Effects, particle emitters, and future persistent renderer-owned scene objects such as weather fields, trails, or procedural scene renderables.

## Do not

Do not serialize live particles, GPU resources, platform controls, tasks, or presenter instances into `GameSnapshot`. Do not implement restore only in one host-specific private loop when the state is shared by multiple hosts.

## Evidence and related documents

- `src/GalNet.Primitives.Builtins/BuiltinPresentationReplay.cs`
- `src/GalNet.Sample.Avalonia/Services/SampleGameSessionService.cs`
- `test/GeneralTest/Runtime/BuiltinPrimitiveInstanceTests.cs`
- `test/GeneralTest/Runtime/GameRuntimeSnapshotTests.cs`
- `docs/spec/runtime.md`
