using System.Text.Json.Serialization;

namespace GalNet.Storage.FileSystem;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(FileSaveService.StoredSave))]
[JsonSerializable(typeof(FileGameProgressService.ProgressData))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;
