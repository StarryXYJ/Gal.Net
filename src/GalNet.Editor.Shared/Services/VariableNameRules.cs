using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using GalNet.Core.Settings;
using GalNet.Core.Variable;
using Serilog;

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
            if (char.IsLetterOrDigit(ch) || ch == '_')
                builder.Append(ch);
        }

        var sanitized = builder.ToString();
        if (sanitized.Length == 0)
            sanitized = fallback;

        if (!char.IsLetter(sanitized[0]) && sanitized[0] != '_')
            sanitized = $"_{sanitized}";

        return sanitized;
    }

    public static void Normalize(ProjectSettings settings)
    {
        Normalize(settings.PlayerVariables, settings.SaveVariables);
    }

    public static void Normalize(
        List<ProjectVariableDefinition> playerVariables,
        List<ProjectVariableDefinition> saveVariables)
    {
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        NormalizeDefinitions(playerVariables, usedNames, "player");
        NormalizeDefinitions(saveVariables, usedNames, "save");
    }

    private static void NormalizeDefinitions(
        List<ProjectVariableDefinition> variables,
        HashSet<string> usedNames,
        string scope)
    {
        for (var i = variables.Count - 1; i >= 0; i--)
        {
            var variable = variables[i];
            var originalName = variable.Name;
            variable.Name = Sanitize(variable.Name, $"var_{scope}_{i + 1}");
            variable.DefaultValue.Name = variable.Name;

            if (!string.Equals(originalName, variable.Name, StringComparison.Ordinal))
                Log.Warning("Sanitized invalid {Scope} variable name from '{Original}' to '{Sanitized}'", scope, originalName, variable.Name);

            if (usedNames.Add(variable.Name))
                continue;

            Log.Warning("Dropped duplicate {Scope} variable definition '{Name}' while normalizing project settings", scope, variable.Name);
            variables.RemoveAt(i);
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ValidNameRegex();
}
