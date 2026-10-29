using System.Security.Cryptography;
using System.Text.Json;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

/// <summary>Author identity shared by the compiler's source maps and the live debugger.</summary>
public static class ProjectAuthorIdentity
{
    public static string Compute(EditorGraphDocument graph, IReadOnlyDictionary<string, List<EditorEntryData>> groups) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Graph = graph,
            Groups = groups.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new
            {
                pair.Key,
                Entries = pair.Value.Select(entry => new { entry.StableId, entry.Type, entry.Condition, entry.Parameters })
            })
        })));
}
