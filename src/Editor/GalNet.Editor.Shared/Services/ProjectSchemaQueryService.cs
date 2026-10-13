using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Commands;

namespace GalNet.Editor.Shared.Services;

/// <summary>Projects a loaded contract into machine descriptions without constructing execution.</summary>
public static class ProjectSchemaQueryService
{
    public static ToolResult Describe(ProjectSchemaContext context, string? type = null, string? kind = null, string? category = null,
        int offset = 0, int? limit = null)
    {
        if (kind is not null && kind is not ("primitive" or "composite")) throw new ArgumentException("Schema kind must be primitive or composite.");
        var diagnostics = new List<ToolDiagnostic>();
        var definitions = context.Catalog.Definitions.Where(definition =>
            (type is null || definition.Type == type) &&
            (kind is null || definition.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase)) &&
            (category is null || definition.Category == category)).OrderBy(definition => definition.Type, StringComparer.Ordinal).ToArray();
        if (type is not null && definitions.Length == 0)
            diagnostics.Add(new("object.notFound", "error", $"Entry schema '{type}' is not declared by this project.", context.AssemblyPath, type));
        var descriptions = definitions.Select(definition =>
        {
            var parameters = definition.DynamicParameters?.Values.Select(parameter =>
            {
                JsonElement? valueSchema = null;
                try { valueSchema = ValueCodec.Describe(parameter.Value); }
                catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
                { diagnostics.Add(new("schema.descriptionLimited", "warning", $"Cannot fully describe '{definition.Type}.{parameter.Name}': {exception.Message}", context.AssemblyPath, definition.Type)); }
                return new SchemaParameterDescription(parameter.Name, parameter.IsRequired, parameter.DefaultValue, parameter.Constraints,
                    valueSchema, valueSchema is not null);
            }).ToArray() ?? [];
            return new SchemaEntryDescription(definition.Type, definition.Category, definition.Kind.ToString().ToLowerInvariant(), definition.IsReading, parameters,
                "Schema descriptions do not serialize custom C# validators or Composite expansion code; full validation/compilation uses the DLL.");
        }).ToArray();
        var page = ProjectQueryService.Page(type is null ? "schema.list" : "schema.show", "project-entry-contract", descriptions, diagnostics, offset, limit);
        return page with { Data = new { contract = new { path = context.AssemblyPath, sha256 = context.Fingerprint }, result = page.Data } };
    }

    public sealed record SchemaEntryDescription(string TypeId, string Category, string Kind, bool IsReading,
        IReadOnlyList<SchemaParameterDescription> Parameters, string ValidationNote);
    public sealed record SchemaParameterDescription(string Name, bool Required, JsonElement? Default,
        JsonElement? Constraints, JsonElement? ValueSchema, bool DescriptionSupported);
}
