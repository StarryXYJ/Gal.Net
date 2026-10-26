using GalNet.Core.Configuration;
using System.Text.Json;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Commands;

public sealed partial class BuiltInEditorCommandHandler
{
    private static CommandExecution RenameProject(EditorProjectDocument document, RenameProjectCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return Error("project.nameRequired", "Project name is required.");
        var oldName = document.Graph.Name;
        document.Graph.Name = command.Name.Trim();
        return Success($"Renamed project '{oldName}' to '{document.Graph.Name}'.", "History.Project.Rename", "project", oldName, document.Graph.Name);
    }

    private static CommandExecution PatchConfiguration(EditorProjectDocument document, PatchConfigurationCommand command,
        ProjectConfigurationSchema configurations)
    {
        var section = configurations.Sections.FirstOrDefault(section => section.Id == command.SectionId);
        if (section is null) return Error("project.configuration.unknownSection", $"Configuration section '{command.SectionId}' is not declared.");
        if (command.Values is null || command.Values.Count + (command.Remove?.Count ?? 0) == 0)
            return Error("project.configuration.emptyPatch", "At least one field change is required.");
        try
        {
            var previous = document.Configuration.Sections.GetValueOrDefault(section.Id);
            if (previous is not null && previous.Version != section.Version)
                throw new InvalidDataException($"Unsupported configuration version {previous.Version} for '{section.Id}'.");
            var fields = section.Fields.Select(field => field.Id).ToHashSet(StringComparer.Ordinal);
            if (command.Values.Keys.Concat(command.Remove ?? []).FirstOrDefault(id => !fields.Contains(id)) is { } unknown)
                return Error("project.configuration.unknownField", $"Configuration field '{section.Id}/{unknown}' is not declared.");
            var values = previous?.Data.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal) ?? new();
            foreach (var id in command.Remove ?? []) values.Remove(id);
            foreach (var (id, value) in command.Values) values[id] = value;
            var candidate = new ProjectSectionData(section.Version, JsonSerializer.SerializeToElement(values));
            section.Validate(candidate);
            document.Configuration.SetRaw(section.Id, candidate);
            return Success($"Updated configuration '{section.Id}'.", "History.ProjectSettings.Patch", ["project/configuration/" + section.Id], command.Values.Count + (command.Remove?.Count ?? 0));
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException)
        { return Error("project.configuration.invalidValue", error.Message); }
    }
}
