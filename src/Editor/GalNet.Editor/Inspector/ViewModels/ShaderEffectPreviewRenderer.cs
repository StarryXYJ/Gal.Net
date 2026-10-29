using System.Text.Json;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Assets;
using GalNet.Rendering.Scene;
using SkiaSharp;

namespace GalNet.Editor.Inspector.ViewModels;

/// <summary>Renders an author's shader with the production texture effect on a fixed sample image.</summary>
internal static class ShaderEffectPreviewRenderer
{
    public const int Size = 192;
    private const int MaximumProgramBytes = 1024 * 1024;

    public static async Task<byte[]> RenderAsync(IAssetManager assets, string programId, string parameters, CancellationToken token)
    {
        var file = await assets.GetFileAsync(programId, token) ?? throw new InvalidDataException("Shader resource was not found.");
        if (file.TypeId != "effect-program" || file.Length > MaximumProgramBytes)
            throw new InvalidDataException("The selected resource is not a supported shader program.");
        var bytes = await file.ReadAllBytesAsync(token);
        token.ThrowIfCancellationRequested();
        if (bytes.Length > MaximumProgramBytes) throw new InvalidDataException("The shader program is too large to preview.");
        var source = new System.Text.UTF8Encoding(false, true).GetString(bytes);
        var resource = new EffectProgramResource(programId);
        var compiled = SkiaShaderEffectProgramLoader.Load(resource, source);
        if (!compiled.IsUsable)
            throw new InvalidDataException(string.Join(Environment.NewLine, compiled.Diagnostics));
        using var runtimeEffect = compiled.RuntimeEffect!;
        var stage = compiled.Descriptor!.Supports(EffectStage.Layer) ? EffectStage.Layer : EffectStage.ScenePost;
        var factory = new ShaderProgramTextureEffectFactory(programId, stage, resource, compiled);
        using var textures = new ShaderTextureBindings();
        using var document = JsonDocument.Parse(parameters);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Shader parameters must be a JSON object.");
        foreach (var parameter in compiled.Descriptor.Parameters.Where(item => item.Kind == ShaderEffectParameterKind.Texture))
        {
            token.ThrowIfCancellationRequested();
            var id = document.RootElement.TryGetProperty(parameter.Name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                if (parameter.Required) throw new InvalidDataException($"Shader texture '{parameter.Name}' is required.");
                continue;
            }
            var lease = await assets.AcquireAsync<SceneTexture>(id, token)
                ?? throw new InvalidDataException($"Shader texture '{id}' could not be loaded.");
            textures.Add(parameter.Name, lease);
        }
        token.ThrowIfCancellationRequested();
        using var sourceBitmap = CreateSample();
        using var output = new SKBitmap(Size, Size);
        using (var canvas = new SKCanvas(output))
        using (var instance = new SceneEffectInstance("editor-preview", factory, "editor-preview", 0, parameters, textures: textures))
        {
            canvas.Clear(SKColors.Transparent);
            instance.Effect.Render(canvas, sourceBitmap, instance);
            canvas.Flush();
        }
        using var image = SKImage.FromBitmap(output);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidDataException("The shader preview could not be encoded.");
        return encoded.ToArray();
    }

    private static SKBitmap CreateSample()
    {
        var image = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(image);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { IsAntialias = true };
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
            {
                paint.Color = (x + y) % 2 == 0 ? new SKColor(235, 238, 245) : new SKColor(205, 213, 228);
                canvas.DrawRect(x * 48, y * 48, 48, 48, paint);
            }
        paint.Color = new SKColor(235, 78, 98);
        canvas.DrawCircle(64, 67, 45, paint);
        paint.Color = new SKColor(44, 137, 228);
        canvas.DrawRoundRect(new SKRect(99, 31, 167, 125), 12, 12, paint);
        paint.Color = new SKColor(245, 190, 60);
        canvas.DrawRect(26, 139, 166, 170, paint);
        return image;
    }
}
