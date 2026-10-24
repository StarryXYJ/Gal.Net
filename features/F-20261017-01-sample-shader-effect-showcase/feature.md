# F-20261017-01 Sample scene shader effect showcase

## Goal

Extend the second route in `GameTestCase` with a walkthrough of scene-post shader effects and Shadertoy-style shaders using the existing effect-program pipeline.

## Scope

- Add sample scene bloom and blur shader programs.
- Keep the scene-post sequence single-effect and give the Shadertoy demos a clean solid-color stage.
- Use a wider Gaussian blur and restrained bloom suitable for the renderer's LDR scene surface.
- Adapt the three user-provided full-screen shaders into scene-post effect programs.
- Demonstrate each effect sequentially on the harbor route, stopping the current effect before starting the next.
- Localize the walkthrough in English and Simplified Chinese and document the sample path.

## Non-goals

- Change Runtime/Core effect contracts or add new renderer stages.
- Add a general Shadertoy import workflow or an effect selector UI.
- Show multiple scene effects at once.

## Acceptance criteria

- Selecting the second route reaches the shader walkthrough.
- Bloom, blur, and all three adapted shaders are invoked as scene-post effects.
- The prior effect is stopped before each next effect is applied; animated time is stopped with its shader.
- Effect descriptions are localized and the GameTestCase README explains the route.
- Shadertoy starts after hiding the route background and portrait and stopping the snow emitter; the route background returns after the last shader.
- Blur uses a normalized Gaussian kernel with a wider pixel radius; bloom uses a soft bright-pass and bounded LDR highlight roll-off.

## Constraints and open questions

- The existing Avalonia host resolves `effect-program` resources and supports scene-post shaders with `source` input.
- The supplied shaders are full-screen generators, so their output must be composited with the existing scene to use the current effect pipeline.
- No architecture-level changes are expected.
