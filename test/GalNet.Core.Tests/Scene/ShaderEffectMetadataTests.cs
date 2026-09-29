using System.Text.Json;
using GalNet.Core.Scene;

namespace GeneralTest.Scene;

public sealed class ShaderEffectMetadataTests
{
    private static readonly EffectProgramResource Program = new("effects/dissolve");

    [Test]
    public void Parser_reads_platform_neutral_parameter_schema()
    {
        var result = ShaderEffectMetadataParser.Parse(Program, """
            /*
            @gal.effect v=1
            @input source
            @targets layer,scenePost

            @param progress
              uniform: uProgress
              type: float
              default: 0
              range: 0..1
              step: 0.01
              animatable: true
              displayName: Reveal

            @texture noise
              uniform: uNoise
              required: true
            */
            uniform shader source;
            uniform float uProgress;
            uniform shader uNoise;
            """);

        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        var descriptor = result.Descriptor!;
        Assert.That(descriptor.Resource, Is.EqualTo(Program));
        Assert.That(descriptor.SourceInput, Is.EqualTo("source"));
        Assert.That(descriptor.Supports(EffectStage.Layer), Is.True);
        Assert.That(descriptor.Supports(EffectStage.ScenePost), Is.True);

        var progress = descriptor.Parameters.Single(parameter => parameter.Name == "progress");
        Assert.Multiple(() =>
        {
            Assert.That(progress.Uniform, Is.EqualTo("uProgress"));
            Assert.That(progress.Kind, Is.EqualTo(ShaderEffectParameterKind.Float));
            Assert.That(progress.Minimum, Is.EqualTo(0));
            Assert.That(progress.Maximum, Is.EqualTo(1));
            Assert.That(progress.Animatable, Is.True);
            Assert.That(progress.DisplayName, Is.EqualTo("Reveal"));
        });
        Assert.That(descriptor.Parameters.Single(parameter => parameter.Name == "noise").Kind, Is.EqualTo(ShaderEffectParameterKind.Texture));
    }

    [Test]
    public void Parser_reports_missing_required_contract_and_malformed_metadata()
    {
        var result = ShaderEffectMetadataParser.Parse(Program, """
            /*
            @gal.effect v=2
            @targets layer,unknown
            @param amount
              uniform: uAmount
              type: float
              range: 4..1
            @param unsupported
              uniform: uUnsupported
              type: number
            */
            """);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Descriptor, Is.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Has.Some.Matches<ShaderEffectMetadataDiagnostic>(diagnostic => diagnostic.Message.Contains("Unsupported @gal.effect version")));
            Assert.That(result.Diagnostics, Has.Some.Matches<ShaderEffectMetadataDiagnostic>(diagnostic => diagnostic.Message.Contains("@input source")));
            Assert.That(result.Diagnostics, Has.Some.Matches<ShaderEffectMetadataDiagnostic>(diagnostic => diagnostic.Message.Contains("Unknown effect target stage")));
            Assert.That(result.Diagnostics, Has.Some.Matches<ShaderEffectMetadataDiagnostic>(diagnostic => diagnostic.Message.Contains("unsupported type")));
            Assert.That(result.Diagnostics, Has.Some.Matches<ShaderEffectMetadataDiagnostic>(diagnostic => diagnostic.Message.Contains("minimum greater")));
        });
    }

    [Test]
    public void Parser_fills_omitted_metadata_and_explicit_values_override_defaults()
    {
        var result = ShaderEffectMetadataParser.Parse(Program, """
            /*
            @gal.effect v=1
            @input source
            @targets layer
            @param amount
              uniform: uAmount
              type: float
            @param enabled
              uniform: uEnabled
              type: bool
              default: true
              animatable: true
            @param mode
              uniform: uMode
              type: enum
              options: Soft,Hard
            @texture noise
              uniform: uNoise
            */
            """);

        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        var parameters = result.Descriptor!.Parameters.ToDictionary(parameter => parameter.Name);
        Assert.Multiple(() =>
        {
            Assert.That(parameters["amount"].DefaultValue, Is.EqualTo("0"));
            Assert.That(parameters["amount"].Step, Is.EqualTo(0.01));
            Assert.That(parameters["amount"].Animatable, Is.True);
            Assert.That(parameters["amount"].DisplayName, Is.EqualTo("amount"));
            Assert.That(parameters["enabled"].DefaultValue, Is.EqualTo("true"));
            Assert.That(parameters["enabled"].Animatable, Is.True);
            Assert.That(parameters["mode"].DefaultValue, Is.EqualTo("Soft"));
            Assert.That(parameters["noise"].DefaultValue, Is.Null);
            Assert.That(parameters["noise"].Animatable, Is.False);
        });
    }

    [Test]
    public void Attachment_keeps_runtime_handle_internal_and_enumerates_static_resource_dependencies()
    {
        var descriptor = ShaderEffectMetadataParser.Parse(Program, """
            /*
            @gal.effect v=1
            @input source
            @targets layer
            @texture mask
              uniform: uMask
            */
            """).Descriptor!;
        var attachment = new ShaderEffectAttachment(
            runtimeHandleId: "runtime:effect:1",
            target: new ShaderEffectTarget.Layer("background"),
            program: Program,
            order: 5,
            parameters: new Dictionary<string, JsonElement>
            {
                ["mask"] = JsonSerializer.SerializeToElement("textures/cloud-noise")
            });

        Assert.Multiple(() =>
        {
            Assert.That(attachment.RuntimeHandleId, Is.EqualTo("runtime:effect:1"));
            Assert.That(attachment.Stage, Is.EqualTo(EffectStage.Layer));
            Assert.That(attachment.EnumerateResourceDependencies(descriptor).Select(resource => resource.Value), Is.EqualTo(new[] { "effects/dissolve", "textures/cloud-noise" }));
        });
    }
}
