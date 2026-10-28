using System.Text.Json.Serialization;

namespace GalNet.Avalonia.GameView.Input;

// Preserve the existing PascalCase properties and numeric device values on disk.
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(PlayerInputConfiguration))]
internal sealed partial class PlayerInputJsonContext : JsonSerializerContext;
