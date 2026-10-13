using GalNet.Editor.Abstraction.Commands;

namespace GalNet.Editor.Shared.Commands;

/// <summary>Parses only arguments advertised by the operation registry.</summary>
public sealed record CliInvocation(ToolOperationDefinition Definition,
    IReadOnlyList<string> Positionals, IReadOnlyDictionary<string, string> Options)
{
    public bool Has(string option) => Options.ContainsKey(option);
    public string? Get(string option) => Options.GetValueOrDefault(option);

    public static CliInvocation Parse(ToolOperationCatalog catalog, IReadOnlyList<string> args)
    {
        if (args.Count == 0) args = ["help"];
        var (definition, consumed) = catalog.Resolve(args);
        var positionals = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = consumed; index < args.Count; index++)
        {
            var token = args[index];
            if (token.StartsWith('-') && token != "-")
            {
                var parameter = definition.Parameters.FirstOrDefault(parameter => !parameter.Positional && parameter.Name == token)
                    ?? throw new ArgumentException($"Unknown option '{token}' for '{definition.CommandPath}'.");
                if (options.ContainsKey(token)) throw new ArgumentException($"Option '{token}' was specified more than once.");
                if (parameter.Type == "boolean") options.Add(token, "true");
                else
                {
                    if (++index >= args.Count || args[index].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"Option '{token}' requires a value.");
                    var value = args[index];
                    if (parameter.Enum is not null && !parameter.Enum.Contains(value, StringComparer.Ordinal))
                        throw new ArgumentException($"Option '{token}' requires one of: {string.Join(", ", parameter.Enum)}.");
                    options.Add(token, value);
                }
            }
            else positionals.Add(token);
        }
        if (definition.OperationId != "help")
        {
            var parameters = definition.Parameters.Where(parameter => parameter.Positional).ToArray();
            if (positionals.Count > parameters.Length) throw new ArgumentException($"Too many arguments for '{definition.CommandPath}'.");
            foreach (var parameter in parameters.Select((parameter, index) => (parameter, index)))
                if (parameter.parameter.Required && positionals.Count <= parameter.index)
                    throw new ArgumentException($"'{definition.CommandPath}' requires <{parameter.parameter.Name}>.");
        }
        else if (positionals.Count > 0 && options.ContainsKey("--search"))
            throw new ArgumentException("help accepts either a command path or --search, not both.");
        return new(definition, positionals.AsReadOnly(), options);
    }
}
