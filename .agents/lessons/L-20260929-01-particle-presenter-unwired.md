# Particle presenter exists but is not reachable from the Builtins module

Status: confirmed  
Source feature: F-20260929-01-compiled-content-pipeline

## Symptom

`GameTestCase` contains `particle.play`, and `AvaloniaGamePageView` implements `StartParticleEmitterAsync` / `StopParticleEmitterAsync`, but the sample cannot show particles.

## Root cause

`BuiltinParticleModule` registers `particle.play` and `particle.stop` with the default immediate primitive factory. Those factories neither update `SceneState` nor call `IParticlePresenter`; the otherwise functional Avalonia presenter is unreachable from content execution.

## Correct approach

Treat an entry schema and a platform presenter as incomplete until a primitive instance connects them. `particle.play` must parse `ParticleEmitterDefinition`, update Runtime-owned emitter/snapshot state, invoke the presenter, and support stop/restore semantics; tests must exercise the module, not only the presenter.

## Scope

Any new Builtins capability that has a Core schema and a platform presenter.

## Resolution

Resolved in `F-20260929-01-compiled-content-pipeline`: `particle.play` / `particle.stop` now use dedicated Primitive instances, update Runtime state, and invoke `IParticlePresenter`. Module-level tests cover the bridge. Persistent save restoration is handled by `BuiltinPresentationReplay`; see `L-20260929-03-persistent-scene-presentation-replay.md`.
