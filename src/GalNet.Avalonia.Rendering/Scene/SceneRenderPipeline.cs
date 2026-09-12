using Avalonia;
using Avalonia.Media;
using GalNet.Core.Scene;
using SkiaSharp;

namespace GalNet.Rendering.Scene;

/// <summary>Fixed ping-pong texture pipeline. Each effect receives only a source texture and writes a target texture.</summary>
public static class SceneRenderPipeline
{
    private static int _gpuFailureReported;
    /// <summary>
    /// Renders directly into Avalonia's active GPU context. Snapshot rendering remains below for
    /// screenshots, tests and backends without a GPU context.
    /// </summary>
    public static bool TryRenderGpu(SKCanvas destination, GRContext? gpuContext, SceneRenderPlan plan, IEnumerable<SceneEffectInstance> effects, IEnumerable<ISceneRenderable>? renderables, Size logicalSize)
    {
        if (gpuContext is null) return false;
        var width = Math.Max(1, (int)Math.Ceiling(logicalSize.Width));
        var height = Math.Max(1, (int)Math.Ceiling(logicalSize.Height));
        var allEffects = effects.ToArray();
        if (allEffects.Any(effect => effect.Effect is not IGpuTextureEffect)) return false;

        try
        {
            var sceneEffects = allEffects.Where(effect => effect.Definition.Stage == EffectStage.ScenePost).ToArray();
            if (sceneEffects.Length == 0)
            {
                destination.Clear(SKColors.Transparent);
                RenderEntriesGpu(destination, gpuContext, plan, allEffects, renderables, width, height);
                return true;
            }

            using var sceneSurface = NewGpuSurface(gpuContext, width, height);
            if (sceneSurface is null) return false;
            sceneSurface.Canvas.Clear(SKColors.Transparent);
            RenderEntriesGpu(sceneSurface.Canvas, gpuContext, plan, allEffects, renderables, width, height);
            sceneSurface.Flush();
            using var input = sceneSurface.Snapshot();
            using var output = ApplyGpuEffects(gpuContext, input, sceneEffects, width, height);
            destination.Clear(SKColors.Transparent);
            destination.DrawImage(output, 0, 0);
            return true;
        }
        catch (Exception error)
        {
            if (Interlocked.Exchange(ref _gpuFailureReported, 1) == 0)
                System.Diagnostics.Trace.TraceWarning("GPU scene pipeline failed; falling back to the snapshot renderer. {0}", error.Message);
            return false;
        }
    }

    public static SKBitmap Render(SceneRenderPlan plan, IEnumerable<SceneEffectInstance> effects, IEnumerable<ISceneRenderable>? renderables, Size logicalSize)
    {
        var width = Math.Max(1, (int)Math.Ceiling(logicalSize.Width));
        var height = Math.Max(1, (int)Math.Ceiling(logicalSize.Height));
        var scene = NewBitmap(width, height);
        using var sceneCanvas = new SKCanvas(scene);
        sceneCanvas.Clear(SKColors.Transparent);
        var allEffects = effects.ToArray();

        foreach (var entry in plan.Items)
        {
            var layerEffects = allEffects
                .Where(effect => effect.Definition.Stage == EffectStage.Layer && effect.TargetHandleId == entry.Layer.HandleId)
                .ToArray();
            if (layerEffects.Length == 0)
            {
                // An empty texture-to-texture chain is an identity pass. Keep the fixed logical
                // pipeline, but do not allocate a full-scene intermediate just to copy it.
                RenderLayer(sceneCanvas, entry.Layer, width, height);
                continue;
            }

            using var layer = NewBitmap(width, height);
            using (var layerCanvas = new SKCanvas(layer)) RenderLayer(layerCanvas, entry.Layer, width, height);
            using var output = ApplyEffects(layer, layerEffects);
            sceneCanvas.DrawBitmap(output, 0, 0);
        }

        if (renderables is not null)
            foreach (var renderable in renderables.OrderBy(renderable => renderable.Z))
                renderable.Render(new SceneRenderContext(sceneCanvas, new SKSize(width, height)));

        var sceneEffects = allEffects.Where(effect => effect.Definition.Stage == EffectStage.ScenePost).ToArray();
        if (sceneEffects.Length == 0) return scene;
        var result = ApplyEffects(scene, sceneEffects);
        scene.Dispose();
        return result;
    }

    private static SKBitmap ApplyEffects(SKBitmap source, IEnumerable<SceneEffectInstance> effects)
    {
        SKBitmap current = source.Copy();
        foreach (var effect in effects.OrderBy(effect => effect.Order).ThenBy(effect => effect.InsertionOrder))
        {
            var next = NewBitmap(source.Width, source.Height);
            using (var canvas = new SKCanvas(next)) effect.Effect.Render(canvas, current, effect);
            current.Dispose();
            current = next;
        }
        return current;
    }

    private static void RenderEntriesGpu(SKCanvas canvas, GRContext gpuContext, SceneRenderPlan plan, IReadOnlyList<SceneEffectInstance> allEffects, IEnumerable<ISceneRenderable>? renderables, int width, int height)
    {
        foreach (var entry in plan.Items)
        {
            var layerEffects = allEffects
                .Where(effect => effect.Definition.Stage == EffectStage.Layer && effect.TargetHandleId == entry.Layer.HandleId)
                .ToArray();
            if (layerEffects.Length == 0)
            {
                RenderLayerGpu(canvas, entry.Layer, width, height);
                continue;
            }

            using var layerSurface = NewGpuSurface(gpuContext, width, height);
            if (layerSurface is null) throw new InvalidOperationException("Unable to allocate a GPU layer surface.");
            layerSurface.Canvas.Clear(SKColors.Transparent);
            RenderLayerGpu(layerSurface.Canvas, entry.Layer, width, height);
            layerSurface.Flush();
            using var input = layerSurface.Snapshot();
            using var output = ApplyGpuEffects(gpuContext, input, layerEffects, width, height);
            canvas.DrawImage(output, 0, 0);
        }

        if (renderables is not null)
            foreach (var renderable in renderables.OrderBy(renderable => renderable.Z))
                renderable.Render(new SceneRenderContext(canvas, new SKSize(width, height)));
    }

    private static SKImage ApplyGpuEffects(GRContext gpuContext, SKImage source, IEnumerable<SceneEffectInstance> effects, int width, int height)
    {
        var current = source;
        foreach (var effect in effects.OrderBy(effect => effect.Order).ThenBy(effect => effect.InsertionOrder))
        {
            using var nextSurface = NewGpuSurface(gpuContext, width, height)
                ?? throw new InvalidOperationException("Unable to allocate a GPU effect surface.");
            effect.Effect.Render(nextSurface.Canvas, current, effect);
            nextSurface.Flush();
            var next = nextSurface.Snapshot();
            current.Dispose();
            current = next;
        }
        return current;
    }

    private static SKSurface? NewGpuSurface(GRContext context, int width, int height) =>
        SKSurface.Create(context, true, new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), 0, GRSurfaceOrigin.TopLeft);

    private static SKBitmap NewBitmap(int width, int height) => new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));

    private static void RenderLayer(SKCanvas canvas, SceneLayerItem item, int width, int height)
    {
        if (!item.IsVisible || item.Opacity <= 0) return;
        using var opacity = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Clamp(Math.Round(item.Opacity * 255), 0, 255)), IsAntialias = true };
        if (!string.IsNullOrWhiteSpace(item.Color))
        {
            var color = Color.Parse(item.Color);
            using var paint = new SKPaint { Color = new SKColor(color.R, color.G, color.B, color.A), ColorFilter = SKColorFilter.CreateBlendMode(opacity.Color, SKBlendMode.Modulate) };
            DrawWithTransform(canvas, item, width, height, item.ScaleX, item.ScaleY, () => canvas.DrawRect(0, 0, width, height, paint));
            return;
        }
        var source = item.Texture?.SkBitmap;
        if (source is null) return;
        var sourceRect = GetSource(source, item.Flipbook);
        var sourceSize = new SKSize(sourceRect.Width, sourceRect.Height);
        var (destination, scaleX, scaleY) = GetDestination(item, sourceSize, width, height);
        DrawWithTransform(canvas, item, width, height, scaleX, scaleY, () =>
        {
            if (item.DisplayMode == LayerDisplayMode.Tile)
            {
                var tileWidth = Math.Max(1, sourceRect.Width * (float)item.ScaleX);
                var tileHeight = Math.Max(1, sourceRect.Height * (float)item.ScaleY);
                for (var y = 0f; y < height; y += tileHeight)
                    for (var x = 0f; x < width; x += tileWidth)
                        canvas.DrawBitmap(source, sourceRect, new SKRect(x, y, x + tileWidth, y + tileHeight), opacity);
            }
            else canvas.DrawBitmap(source, sourceRect, destination, opacity);
        });
    }

    private static void RenderLayerGpu(SKCanvas canvas, SceneLayerItem item, int width, int height)
    {
        if (!item.IsVisible || item.Opacity <= 0) return;
        using var opacity = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Clamp(Math.Round(item.Opacity * 255), 0, 255)), IsAntialias = true };
        if (!string.IsNullOrWhiteSpace(item.Color))
        {
            var color = Color.Parse(item.Color);
            using var paint = new SKPaint { Color = new SKColor(color.R, color.G, color.B, color.A), ColorFilter = SKColorFilter.CreateBlendMode(opacity.Color, SKBlendMode.Modulate) };
            DrawWithTransform(canvas, item, width, height, item.ScaleX, item.ScaleY, () => canvas.DrawRect(0, 0, width, height, paint));
            return;
        }
        var source = item.Texture?.SkImage;
        if (source is null) return;
        var sourceRect = GetSource(source.Width, source.Height, item.Flipbook);
        var sourceSize = new SKSize(sourceRect.Width, sourceRect.Height);
        var (destination, scaleX, scaleY) = GetDestination(item, sourceSize, width, height);
        DrawWithTransform(canvas, item, width, height, scaleX, scaleY, () =>
        {
            if (item.DisplayMode == LayerDisplayMode.Tile)
            {
                var tileWidth = Math.Max(1, sourceRect.Width * (float)item.ScaleX);
                var tileHeight = Math.Max(1, sourceRect.Height * (float)item.ScaleY);
                for (var y = 0f; y < height; y += tileHeight)
                    for (var x = 0f; x < width; x += tileWidth)
                        canvas.DrawImage(source, sourceRect, new SKRect(x, y, x + tileWidth, y + tileHeight), opacity);
            }
            else canvas.DrawImage(source, sourceRect, destination, opacity);
        });
    }

    private static SKRect GetSource(SKBitmap bitmap, FlipbookDefinition? flipbook)
    {
        return GetSource(bitmap.Width, bitmap.Height, flipbook);
    }

    private static SKRect GetSource(int width, int height, FlipbookDefinition? flipbook)
    {
        if (flipbook is not { IsValid: true }) return new SKRect(0, 0, width, height);
        var cellWidth = width / flipbook.Columns;
        var cellHeight = height / flipbook.Rows;
        var index = flipbook.CurrentFrameIndex;
        return new SKRect((index % flipbook.Columns) * cellWidth, (index / flipbook.Columns) * cellHeight, ((index % flipbook.Columns) + 1) * cellWidth, ((index / flipbook.Columns) + 1) * cellHeight);
    }

    private static (SKRect Destination, float ScaleX, float ScaleY) GetDestination(SceneLayerItem item, SKSize source, int width, int height)
    {
        var native = new SKSize(Math.Max(1, source.Width * (float)item.ScaleX), Math.Max(1, source.Height * (float)item.ScaleY));
        return item.DisplayMode switch
        {
            LayerDisplayMode.Native => (Center(native, width, height), 1, 1),
            LayerDisplayMode.Fill => (new SKRect(0, 0, width, height), (float)item.ScaleX, (float)item.ScaleY),
            LayerDisplayMode.Uniform => (Contain(native, width, height, false), 1, 1),
            LayerDisplayMode.UniformToFill => (Contain(native, width, height, true), 1, 1),
            LayerDisplayMode.Tile => (new SKRect(0, 0, width, height), 1, 1),
            _ => (new SKRect(0, 0, width, height), 1, 1)
        };
    }
    private static SKRect Center(SKSize size, int width, int height) => new((width - size.Width) / 2, (height - size.Height) / 2, (width + size.Width) / 2, (height + size.Height) / 2);
    private static SKRect Contain(SKSize source, int width, int height, bool fill)
    {
        var scale = fill ? Math.Max(width / source.Width, height / source.Height) : Math.Min(width / source.Width, height / source.Height);
        return Center(new SKSize(source.Width * scale, source.Height * scale), width, height);
    }
    private static void DrawWithTransform(SKCanvas canvas, SceneLayerItem item, int width, int height, double scaleX, double scaleY, Action draw)
    {
        canvas.Save();
        canvas.Translate(width / 2f + (float)item.X, height / 2f + (float)item.Y);
        canvas.RotateDegrees((float)item.RotationDegrees);
        canvas.Scale((float)scaleX, (float)scaleY);
        canvas.Translate(-width / 2f, -height / 2f);
        draw();
        canvas.Restore();
    }
}
