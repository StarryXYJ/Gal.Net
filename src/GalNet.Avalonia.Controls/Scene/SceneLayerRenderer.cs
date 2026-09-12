using Avalonia;
using Avalonia.Media;
using GalNet.Core.Scene;

namespace GalNet.Game.Controls.Scene;

/// <summary>Temporary Avalonia draw backend. It is the only scene type that knows Avalonia drawing APIs.</summary>
internal static class SceneLayerRenderer
{
    private static readonly IBrush MissingLayerBrush = new SolidColorBrush(Color.Parse("#662A2D42"));

    public static void Render(DrawingContext context, SceneLayerItem item, Size surface)
    {
        if (!item.IsVisible || item.Opacity <= 0 || surface.Width <= 0 || surface.Height <= 0) return;
        using var opacity = context.PushOpacity(Math.Clamp(item.Opacity, 0, 1));
        if (!string.IsNullOrWhiteSpace(item.Color))
        {
            using var colorTransform = context.PushTransform(CreateTransform(item, surface, item.ScaleX, item.ScaleY));
            context.DrawRectangle(new SolidColorBrush(Color.Parse(item.Color)), null, new Rect(surface));
            return;
        }

        if (item.Image is null) { context.DrawRectangle(MissingLayerBrush, null, new Rect(surface)); return; }

        var (source, sourceSize) = GetSource(item.Image, item.Flipbook);
        var (destination, transformScaleX, transformScaleY) = GetDestination(item, sourceSize, surface);
        using var transform = context.PushTransform(CreateTransform(item, surface, transformScaleX, transformScaleY));
        if (item.DisplayMode == LayerDisplayMode.Tile) { DrawTiled(context, item, source, sourceSize, surface); return; }
        DrawMasked(context, item, source, destination, surface);
    }

    private static (Rect Source, Size Size) GetSource(IImage image, FlipbookDefinition? flipbook)
    {
        if (flipbook is not { IsValid: true }) return (new Rect(image.Size), image.Size);
        var width = image.Size.Width / flipbook.Columns;
        var height = image.Size.Height / flipbook.Rows;
        var index = flipbook.CurrentFrameIndex;
        return (new Rect((index % flipbook.Columns) * width, (index / flipbook.Columns) * height, width, height), new Size(width, height));
    }

    private static (Rect Destination, double TransformScaleX, double TransformScaleY) GetDestination(SceneLayerItem item, Size source, Size surface)
    {
        var native = new Size(Math.Max(1, source.Width * item.ScaleX), Math.Max(1, source.Height * item.ScaleY));
        return item.DisplayMode switch
        {
            LayerDisplayMode.Native => (Center(native, surface), 1, 1),
            LayerDisplayMode.Fill => (new Rect(surface), item.ScaleX, item.ScaleY),
            LayerDisplayMode.Uniform => (Contain(native, surface, false), 1, 1),
            LayerDisplayMode.UniformToFill => (Contain(native, surface, true), 1, 1),
            LayerDisplayMode.Tile => (new Rect(surface), 1, 1),
            _ => (new Rect(surface), 1, 1)
        };
    }

    private static Rect Center(Size size, Size surface) => new((surface.Width - size.Width) / 2, (surface.Height - size.Height) / 2, size.Width, size.Height);

    private static Rect Contain(Size source, Size surface, bool fill)
    {
        var scale = fill ? Math.Max(surface.Width / source.Width, surface.Height / source.Height) : Math.Min(surface.Width / source.Width, surface.Height / source.Height);
        return Center(new Size(source.Width * scale, source.Height * scale), surface);
    }

    private static void DrawTiled(DrawingContext context, SceneLayerItem item, Rect source, Size sourceSize, Size surface)
    {
        var width = Math.Max(1, sourceSize.Width * item.ScaleX);
        var height = Math.Max(1, sourceSize.Height * item.ScaleY);
        for (var y = 0d; y < surface.Height; y += height)
            for (var x = 0d; x < surface.Width; x += width)
                context.DrawImage(item.Image!, source, new Rect(x, y, width, height));
    }

    private static void DrawMasked(DrawingContext context, SceneLayerItem item, Rect source, Rect destination, Size surface)
    {
        if (item.BlindsBladeCount <= 0) { context.DrawImage(item.Image!, source, destination); return; }
        var blades = Math.Max(1, item.BlindsBladeCount);
        var progress = Math.Clamp(item.BlindsProgress, 0, 1);
        for (var index = 0; index < blades; index++)
        {
            var clip = item.BlindsHorizontal
                ? new Rect(0, index * (surface.Height / blades), surface.Width, (surface.Height / blades) * progress)
                : new Rect(index * (surface.Width / blades), 0, (surface.Width / blades) * progress, surface.Height);
            using (context.PushClip(clip)) context.DrawImage(item.Image!, source, destination);
        }
    }

    private static Matrix CreateTransform(SceneLayerItem item, Size surface, double scaleX, double scaleY)
    {
        var radians = item.RotationDegrees * Math.PI / 180;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var m11 = scaleX * cosine;
        var m12 = scaleX * sine;
        var m21 = -scaleY * sine;
        var m22 = scaleY * cosine;
        var centerX = surface.Width / 2;
        var centerY = surface.Height / 2;
        return new Matrix(m11, m12, m21, m22, centerX + item.X - ((centerX * m11) + (centerY * m21)), centerY + item.Y - ((centerX * m12) + (centerY * m22)));
    }
}
