using Avalonia.Media;
using GalNet.Core.Parameters;
using GalNet.Editor.Themes;

namespace GeneralTest.Editor;

public sealed class EditorUiuxTests
{
    [Test]
    public void TransparentHoverDoesNotCreateALightIntermediateSurface()
    {
        var background = Color.Parse("#181825");
        var target = Color.Parse("#313244");
        double Composite(Color color, byte component, byte surface) => component * color.A / 255d + surface * (1 - color.A / 255d);
        // Regression evidence: the framework's unpremultiplied interpolation leaks transparent white.
        var animatorType = typeof(Color).Assembly.GetType("Avalonia.Animation.Animators.ColorAnimator")!;
        var framework = (Color)animatorType.GetMethod("Interpolate")!.Invoke(Activator.CreateInstance(animatorType), [0.5, Colors.Transparent, target])!;
        Assert.That(Composite(framework, framework.R, background.R), Is.GreaterThan(target.R));
        foreach (var progress in new[] { 0d, 0.1, 0.25, 0.5, 0.75, 0.9, 1 })
        {
            var brush = (ISolidColorBrush)LinearBrushTransition.Blend(progress, Brushes.Transparent, new SolidColorBrush(target))!;
            var color = brush.Color;
            Assert.That(Composite(color, color.R, background.R), Is.InRange((double)background.R, target.R));
            Assert.That(Composite(color, color.G, background.G), Is.InRange((double)background.G, target.G));
            Assert.That(Composite(color, color.B, background.B), Is.InRange((double)background.B, target.B));
            var reversed = (ISolidColorBrush)LinearBrushTransition.Blend(0.5, brush, Brushes.Transparent)!;
            Assert.That(Composite(reversed.Color, reversed.Color.R, background.R), Is.LessThanOrEqualTo(Composite(color, color.R, background.R)));
        }
    }

    [Test]
    public void CommonAnnotationFlowsThroughInheritedGeneratedSchema()
    {
        Assert.That(CommonDerivedOptions.Schema.Single(field => field.Id == "title").Editor!.IsCommon, Is.True);
        Assert.That(CommonDerivedOptions.Schema.Single(field => field.Id == "advanced").Editor!.IsCommon, Is.False);
        Assert.That(new EditorHints(IsCommon: true).IsCommon, Is.True);
        var legacyConstructor = typeof(EditorHints).GetConstructor([typeof(string), typeof(double?), typeof(string), typeof(string)]);
        Assert.That(legacyConstructor, Is.Not.Null, "Previously compiled contract metadata keeps its constructor ABI.");
        var legacy = (EditorHints)legacyConstructor!.Invoke(["text", null, null, null]);
        Assert.That(legacy.IsCommon, Is.False);
    }
}

public abstract class CommonBaseOptions
{
    [Field(0), Common] public string? Title { get; set; }
}

[GenerateFieldSchema]
public partial class CommonDerivedOptions : CommonBaseOptions
{
    [Field(1)] public float Advanced { get; set; }
}
