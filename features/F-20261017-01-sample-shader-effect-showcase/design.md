# Design

The showcase belongs to the existing `GameTestCase` harbor group, which is reached by the second route choice. Each example is a project-owned `effect-program` asset referenced by its stable metadata ID.

Bloom, blur, and the adapted full-screen shaders declare `@input source` and `@targets scenePost`; the Avalonia effect runtime compiles and runs them through the current scene-post pipeline. The three Shadertoy programs keep their supplied `iTime` uniform and use the renderer-provided `size` uniform for resolution. A lightweight wrapper blends their generated image over the current scene. Their logical `time` metadata parameter is driven by a looping `animation.animate` entry. The built-in animation schema exposes this common shader time property because program-specific metadata cannot be enumerated at compile time.

The group applies one visual demonstration at a time and pauses on a localized dialogue line before moving on. Blur is implemented with two internal scene-post passes (horizontal, then vertical) that together form one separable Gaussian effect. Before stopping an animated shader it also stops its animation playback.

Before the Shadertoy portion, the group shows a full-canvas solid-color layer above the scene, hides the route background and portrait, and stops the snow emitter. The color layer masks any remaining one-shot particles while the scene-post shader uses it as its source. After the last shader stops, the route continues directly into the ending while keeping the solid stage; it does not restore the harbor scene.

The renderer's scene surfaces use 8-bit `RGBA8888`, so they carry LDR values and cannot provide true HDR data for tone mapping. Bloom therefore uses a high-threshold soft-knee bright pass, a 40-pixel Gaussian spread, and remaining per-channel headroom to keep its output bounded. Blur uses a standard separable Gaussian kernel: five iteration bands generate 15 dense weighted samples on each side of the center in the horizontal pass, followed by the same kernel vertically. Its 28-pixel radius controls the outer sample distance.
