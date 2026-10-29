using Avalonia;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Presentation.Abstractions.View;
using GalNet.Rendering.Scene;
using SkiaSharp;

namespace GeneralTest.Presentation;

public sealed class VideoLayerTests
{
    [Test]
    public void Seek_uses_current_position_and_pending_target_and_stops_at_duration()
    {
        var cursor = new VideoSkipCursor([5, 12, 20]);
        Assert.That(cursor.Next(8, 30), Is.EqualTo(12));
        Assert.That(cursor.Next(8, 30), Is.EqualTo(20));
        Assert.That(cursor.Next(8, 30), Is.Null);
        Assert.That(new VideoSkipCursor([10]).Next(0, 10), Is.Null);
        Assert.That(new VideoSkipCursor([11]).Next(0, 10), Is.Null);
        Assert.That(new VideoSkipCursor([]).Next(0, 10), Is.Null);
        Assert.That(new VideoSkipCursor([5]).Next(5, 10), Is.Null);
        Assert.That(new VideoSkipCursor([5]).Next(0, null), Is.EqualTo(5));
    }

    [Test]
    public void Dynamic_frames_share_layer_sorting_transform_and_opacity()
    {
        using var frames = new SceneFrameSource();
        frames.Publish(Frame(SKColors.Red));
        var video = new SceneLayerItem { HandleId = "video", FrameSource = frames, Z = 5, DisplayMode = LayerDisplayMode.Native, X = 2, Opacity = .5 };
        var background = new SceneLayerItem { HandleId = "bg", Color = "#0000FF", Z = 0 };
        var plan = SceneRenderPlan.Create([new SceneRenderEntry(video, 0), new SceneRenderEntry(background, 1)]);
        using (var scene = SceneRenderPipeline.Render(plan, [], null, new Size(8, 4)))
        {
            Assert.That(scene.GetPixel(0, 2), Is.EqualTo(SKColors.Blue));
            Assert.That(scene.GetPixel(5, 2).Red, Is.InRange(126, 129));
            Assert.That(scene.GetPixel(5, 2).Blue, Is.InRange(126, 129));
        }
        frames.Publish(Frame(SKColors.Lime));
        using var updated = SceneRenderPipeline.Render(plan, [], null, new Size(8, 4));
        Assert.That(updated.GetPixel(5, 2).Green, Is.InRange(126, 129));
        frames.Dispose();
        using var stopped = SceneRenderPipeline.Render(plan, [], null, new Size(8, 4));
        Assert.That(stopped.GetPixel(5, 2), Is.EqualTo(SKColors.Blue));
    }

    [Test]
    public void Video_frames_participate_in_layer_effect_and_scene_post_passes()
    {
        using var frames = new SceneFrameSource();
        frames.Publish(Frame(SKColors.Red));
        var layer = new SceneLayerItem { HandleId = "video", FrameSource = frames, DisplayMode = LayerDisplayMode.Fill };
        var plan = SceneRenderPlan.Create([new SceneRenderEntry(layer, 0)]);
        var effect = new SceneEffectInstance("video-effect", new FillEffectFactory(EffectStage.Layer, SKColors.Lime), "video", 0, "{}");
        using var local = SceneRenderPipeline.Render(plan, [effect], null, new Size(4, 4));
        Assert.That(local.GetPixel(2, 2), Is.EqualTo(SKColors.Lime));
        var post = new SceneEffectInstance("post", new FillEffectFactory(EffectStage.ScenePost, SKColors.Blue), "", 0, "{}");
        using var scene = SceneRenderPipeline.Render(plan, [effect, post], null, new Size(4, 4));
        Assert.That(scene.GetPixel(2, 2), Is.EqualTo(SKColors.Blue));
    }

    [Test]
    public async Task A_render_lease_keeps_the_current_frame_alive_until_released()
    {
        using var frames = new SceneFrameSource();
        frames.Publish(Frame(SKColors.Red));
        var lease = frames.AcquireFrame();
        var updating = Task.Run(() => frames.Publish(Frame(SKColors.Blue)));
        Assert.That(lease.Texture!.SkBitmap!.GetPixel(0, 0), Is.EqualTo(SKColors.Red));
        lease.Dispose();
        await updating.WaitAsync(TimeSpan.FromSeconds(3));
        using var next = frames.AcquireFrame();
        Assert.That(next.Texture!.SkBitmap!.GetPixel(0, 0), Is.EqualTo(SKColors.Blue));
    }

    private static SKBitmap Frame(SKColor color)
    {
        var frame = new SKBitmap(4, 4);
        frame.Erase(color);
        return frame;
    }

    private sealed class FillEffectFactory(EffectStage stage, SKColor color) : ITextureEffectFactory
    {
        public EffectDefinition Definition { get; } = new("test.fill", stage, [], []);
        public ITextureEffect Create() => new FillEffect(color);
    }

    private sealed class FillEffect(SKColor color) : ITextureEffect
    {
        public void Render(SKCanvas target, SKBitmap source, SceneEffectInstance instance) => target.DrawColor(color);
    }
}
