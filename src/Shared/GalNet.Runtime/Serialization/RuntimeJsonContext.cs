using System.Text.Json.Serialization;
using GalNet.Runtime.Loader;

namespace GalNet.Runtime.Serialization;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(GraphLoader.GraphDto))]
internal sealed partial class RuntimeJsonContext : JsonSerializerContext;
