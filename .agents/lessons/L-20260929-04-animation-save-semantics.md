# Animation saves should preserve stable meaning, not a presentation cursor

Status: confirmed  
Source feature: F-20260929-01-compiled-content-pipeline

## Symptom

Looping animations were written to `SceneState` with their terminal value and only a partial replay description. A save could therefore restore a loop at the wrong logical value and could not reconstruct its duration, curve, loop mode, blend mode, or complete plan.

## Root cause

The original "non-blocking primitives commit their final value before returning" rule treated one-shot and looping presentation as equivalent. A one-shot animation has a stable terminal state, while an infinite loop has no terminal state and must instead retain a stable restart state plus its complete definition.

## Correct approach

- Non-blocking one-shot animations commit their terminal logical value before a stable snapshot.
- Looping animations keep the frame-zero logical state and save a complete, platform-neutral replay definition.
- Restoring a loop recreates its presentation from frame zero; it does not serialize a wall-clock cursor, active task, renderer resource, or individual particle.
- Blocking animations do not need an in-flight save representation because the engine keeps the previous stable snapshot while they are active.
- If a future media type genuinely needs cursor resume, make that an explicit opt-in restore policy instead of changing the default animation contract.

## Scope

Immediate animations, animation plans, particle/effect property animation, and future persistent visual loops.

## Do not

Do not save a frame number alone and call it resumable state: plan events, ping-pong direction, random simulation state, and content-version changes make that representation incomplete.

## Evidence and related documents

- `src/GalNet.Primitives.Builtins/BuiltinRuntimeActions.cs`
- `src/GalNet.Primitives.Builtins/BuiltinPresentationReplay.cs`
- `docs/spec/runtime.md`
- User-confirmed restore semantics in F-20260929-01
