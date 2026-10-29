using System.Text.Json.Serialization;
using GalNet.Assets.Variants;
using GalNet.Contracts.Builtins.Variants;

namespace GalNet.Assets;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ImageVariantsSource))]
[JsonSerializable(typeof(ImageVariantsCodec.Payload))]
internal sealed partial class AssetsJsonContext : JsonSerializerContext;
