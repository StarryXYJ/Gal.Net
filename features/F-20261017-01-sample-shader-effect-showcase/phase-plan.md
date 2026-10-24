# Phase plan

## P1 — Add the sequential sample showcase

**Status:** verified

Add bloom and blur programs, adapt the three supplied shaders to the existing scene-post contract, append their ordered apply/show/stop entries to the second route, localize the dialogue, and update the sample README.

**Exit conditions:** all five effect programs have unique `effect-program` metadata IDs; the route stops each visual demonstration before beginning another (the paired Gaussian passes count as one Blur demonstration); each animated shader's time animation stops before its effect; English and Chinese keys cover the walkthrough; the sample content builds successfully.

**Evidence:** the Headless CLI built `GameTestCase` successfully with four compiled groups and no diagnostics. The Headless CLI project and Sample.Contracts deployment both built with zero errors. Interactive shader compilation/rendering was not exercised.

## P2 — Refine the scene stage and LDR shader examples

**Status:** verified

Give the Shadertoy examples a clean solid-color stage while hiding the route background and portrait and stopping the persistent snow emitter. Continue directly into the ending without restoring the route scene. Replace the compact blur kernel with five wider Gaussian sampling steps, keep bloom visible but bounded for the renderer's LDR surface, and reduce Shadertoy program size to fit Skia RuntimeEffect.

**Exit conditions:** Shadertoy runs over a solid-color full-canvas layer with other route visuals hidden; the route flows into ending on that stage; bloom remains bounded and visibly targets bright highlights; blur uses five Gaussian-weighted sampling steps; all effect programs compile and content build succeeds.

**Evidence:** all five sample shaders compiled through Skia `SKRuntimeEffect`; Bloom, Blur, Tunnel, Grid, and Rain were rendered in the Skia CPU preview. The harbor group and English/Chinese localization parsed successfully; only the two internal Blur passes overlap, and no other visual demonstrations overlap them. The Headless CLI built four groups without diagnostics. The final LDR Bloom preview keeps the halo around bright windows bounded and localized.
