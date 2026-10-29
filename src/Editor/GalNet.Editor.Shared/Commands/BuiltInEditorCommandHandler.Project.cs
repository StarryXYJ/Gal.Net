using System.Text.Json;
using GalNet.Core.Configuration;
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
            document.Configuration.Patch(section, command.Values, command.Remove);
            return Success($"Updated configuration '{section.Id}'.", "History.ProjectSettings.Patch", ["project/configuration/" + section.Id], command.Values.Count + (command.Remove?.Count ?? 0));
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException)
        { return Error("project.configuration.invalidValue", error.Message); }
    }
}
