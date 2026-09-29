using System.Text.Json;
using GalNet.Core.I18n;
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

    private static CommandExecution PatchSettings(EditorProjectDocument document, PatchProjectSettingsCommand command)
    {
        if (command.Values.Count == 0)
            return Error("project.settings.emptyPatch", "At least one project setting is required.");
        foreach (var (key, value) in command.Values)
        {
            try
            {
                switch (key)
                {
                    case "defaultWidth":
                        document.Settings.DefaultWidth = PositiveInt(value, key);
                        break;
                    case "defaultHeight":
                        document.Settings.DefaultHeight = PositiveInt(value, key);
                        break;
                    case "saveSlotCount":
                        document.Settings.SaveSlotCount = NonNegativeInt(value, key);
                        break;
                    case "sfxChannelCount":
                        document.Settings.SfxChannelCount = PositiveInt(value, key);
                        break;
                    case "targetLocale":
                        var locale = new I18nLocale(value.GetString() ?? throw new JsonException("A locale string is required."));
                        document.Settings.TargetLocale = locale;
                        if (!document.Settings.AvailableLocales.Contains(locale))
                            document.Settings.AvailableLocales.Add(locale);
                        break;
                    default:
                        return Error("project.settings.unknownField", $"Project setting '{key}' is not editable through this command.");
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
            {
                return Error("project.settings.invalidValue", $"Invalid value for '{key}': {exception.Message}");
            }
        }
        return Success($"Updated {command.Values.Count} project setting(s).", "History.ProjectSettings.Patch", ["project/settings"], command.Values.Count);
    }

    private static int PositiveInt(JsonElement value, string key)
    {
        var result = value.GetInt32();
        return result > 0 ? result : throw new InvalidOperationException($"'{key}' must be greater than zero.");
    }

    private static int NonNegativeInt(JsonElement value, string key)
    {
        var result = value.GetInt32();
        return result >= 0 ? result : throw new InvalidOperationException($"'{key}' cannot be negative.");
    }

}
