using System.Globalization;
using System.Text;
using System.Text.Json;
using GalNet.Editor.Abstraction.Commands;

namespace GalNet.Editor.Shared.Commands;

public static class ToolHelp
{
    public static IReadOnlyList<ToolOperationDefinition> Select(ToolOperationCatalog catalog, CliInvocation invocation)
    {
        if (invocation.Positionals.Count == 0) return catalog.Search(invocation.Get("--search"));
        var target = string.Join(' ', invocation.Positionals);
        return [catalog.Find(target) ?? throw new ArgumentException($"Unknown command or operation '{target}'.")];
    }

    public static string RenderText(IReadOnlyList<ToolOperationDefinition> definitions, bool detailed)
    {
        var text = new StringBuilder("GalNet project tools\n");
        foreach (var definition in definitions)
        {
            text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"{definition.OperationId} ({definition.Transport})")
                .AppendLine(CultureInfo.InvariantCulture, $"  {definition.Description}").AppendLine(CultureInfo.InvariantCulture, $"  {definition.Usage}");
            if (!detailed) continue;
            foreach (var parameter in definition.Parameters)
                text.AppendLine(CultureInfo.InvariantCulture, $"  {parameter.Name}: {parameter.Type}; required={parameter.Required}. {parameter.Description}");
            text.AppendLine(CultureInfo.InvariantCulture, $"  Project={definition.RequiresProject}; contract={definition.RequiresContract}; readOnly={definition.ReadOnly}; dryRun={definition.SupportsDryRun}")
                .AppendLine(CultureInfo.InvariantCulture, $"  Batch: {definition.BatchMode}")
                .AppendLine(CultureInfo.InvariantCulture, $"  Input: {definition.InputSchema.GetRawText()}");
            if (definition.DocumentInputSchema is { } documentInput)
                text.AppendLine(CultureInfo.InvariantCulture, $"  Document input (execute): {documentInput.GetRawText()}");
            foreach (var example in definition.Examples) text.AppendLine(CultureInfo.InvariantCulture, $"  Example: {example}");
            foreach (var exit in definition.ExitCodes) text.AppendLine(CultureInfo.InvariantCulture, $"  Exit {exit.Key}: {exit.Value}");
        }
        text.AppendLine().AppendLine("Use help <command-path>, help <operationId>, or help --search <keyword> --json.");
        return text.ToString();
    }
}
