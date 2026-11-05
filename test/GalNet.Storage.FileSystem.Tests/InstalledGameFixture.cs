using System.Text.Json;
using GalNet.Assets;
using GalNet.Core.Serialization;

namespace GeneralTest.Storage;

internal static class InstalledGameFixture
{
    public static void Write(string directory, string projectId, char build = 'a')
    {
        var hash = new string(build, 64);
        var asset = new GalpakFileEntry("Assets/Paks/base.pak", hash, 0);
        var language = new GalpakFileEntry(PackedGameDocuments.LocalizationFileName, hash, 0);
        var content = new GameContentManifest { Version = 3, ProjectId = projectId, BuildId = hash, BasePaks = [asset], LocalizationPak = language };
        var installation = new GalpakManifest(4, projectId, projectId, DateTimeOffset.UnixEpoch, [asset, language]);
        var documents = new Dictionary<string, byte[]>
        {
            [GameContentManifest.FileName] = JsonSerializer.SerializeToUtf8Bytes(content, JsonSerializerOptions.Web),
            [PackedGameDocuments.InstallationPath] = JsonSerializer.SerializeToUtf8Bytes(installation, JsonSerializerOptions.Web),
            ["settings.json"] = "{}"u8.ToArray(), ["gallery.json"] = "{}"u8.ToArray(),
            ["characters.json"] = "{}"u8.ToArray(), ["packaging-layout.json"] = "{}"u8.ToArray()
        };
        File.WriteAllBytes(Path.Combine(directory, PackedGameDocuments.GameFileName), PackedGameDocuments.Build(projectId, hash, documents));
    }
}
