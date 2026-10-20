using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace GalNet.Editor.Shared.Services;

/// <summary>Author-only source revision evidence. Translation values are never overwritten by source edits.</summary>
public static class LocalizationTextState
{
    public static string Fingerprint(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static byte[] Update(byte[] metadata, string key, string culture, string? previous, string text, IEnumerable<string> translatedCultures)
    {
        LocalizationJson.Validate(LocalizationJson.MetadataPath, metadata);
        var root = JsonNode.Parse(LocalizationJson.Decode(metadata))!.AsObject();
        var states = root["textStates"] as JsonObject ?? new(); root["textStates"] = states;
        if (states[key] is not JsonObject state)
        {
            state = new JsonObject { ["sourceLocale"] = culture, ["translations"] = new JsonObject() }; states[key] = state;
        }
        var translations = state["translations"]!.AsObject();
        foreach (var language in translatedCultures.Where(language => !language.Equals(culture, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!translations.ContainsKey(language)) translations[language] = previous is null || previous == text ? null : Fingerprint(previous);
        state["sourceLocale"] = culture; state["sourceFingerprint"] = Fingerprint(text);
        return LocalizationJson.WriteLike(metadata, root);
    }
    public static bool? NeedsReview(byte[] metadata, string key, string culture)
    {
        var root = JsonNode.Parse(LocalizationJson.Decode(metadata));
        if (root?["textStates"]?[key] is not JsonObject state || state["translations"]?[culture] is not JsonValue reviewed) return null;
        return !string.Equals(reviewed.GetValue<string>(), state["sourceFingerprint"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    }
}
