# Skia DrawAtlas transform uses source-local coordinates

Status: confirmed  
Source feature: F-20260929-02-particle-system-v2

## Symptom

Adding rotation around the sprite center made flipbook frame 0 render correctly, while later atlas frames were shifted away from the particle position or disappeared.

## Root cause

`SKCanvas.DrawAtlas` applies each `SKRotationScaleMatrix` to coordinates local to that entry's source rectangle. Using `source.MidX` / `source.MidY` includes the rectangle's atlas offset a second time.

## How it was confirmed

The existing actual-pixel flipbook test failed only on the second source rect after centered rotation was introduced. Replacing the pivot with `source.Width / 2` and `source.Height / 2` restored the expected pixel while retaining the rotation test.

## Correct approach

Build the centered transform from the source rectangle's width and height. Keep atlas-space `Left` / `Top` only in the `SKRect` passed to `DrawAtlas`.

## Scope

Particle flipbooks and any future atlas renderer that combines source rectangles with rotation, scaling, or alignment.

## Do not

Do not use atlas-space `MidX` / `MidY` as the transform pivot for an individual `DrawAtlas` entry.

## Evidence

- `test/GeneralTest/Scene/ParticleEmitterTests.cs`
- `src/GalNet.Avalonia.Rendering/Scene/ParticleEmitter.cs`
