using System;
using System.Text;
using System.Text.RegularExpressions;

namespace GalNet.Editor.Shared.Services;

public static partial class VariableNameRules
{
    private static readonly Regex NamePattern = ValidNameRegex();

    public static bool IsValid(string? name) =>
        !string.IsNullOrWhiteSpace(name) && NamePattern.IsMatch(name);

    public static string Sanitize(string? name, string fallback = "var")
    {
        var raw = (name ?? string.Empty).Trim();
        if (raw.Length == 0)
            return fallback;

        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (IsAsciiLetter(ch) || char.IsAsciiDigit(ch) || ch == '_')
                builder.Append(ch);
        }

        var sanitized = builder.ToString();
        if (sanitized.Length == 0)
            sanitized = fallback;

        return sanitized;
    }

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex ValidNameRegex();

    private static bool IsAsciiLetter(char ch) =>
        ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}
