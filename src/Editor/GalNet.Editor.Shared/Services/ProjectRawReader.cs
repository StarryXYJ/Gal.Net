using System.Text.Json;
using GalNet.Editor.Abstraction.Commands;

namespace GalNet.Editor.Shared.Services;

/// <summary>Raw, read-only project access. Never repairs IDs or filters unknown content.</summary>
public sealed class ProjectRawReader
{
    private static readonly HashSet<string> ExcludedRoots = new(StringComparer.OrdinalIgnoreCase)
    { "Output", "Temp", ".galnet", ".editor-tx", "Contracts", ".git", ".vs", ".idea" };
    private readonly StringComparison _pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public ProjectRawReader(string projectDirectory)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectDirectory));
        if (!Directory.Exists(Root)) throw new DirectoryNotFoundException($"Project directory not found: '{Root}'.");
        if ((File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Project root must not be a directory link.");
    }

    public string Root { get; }
    public string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    public string Resolve(string relative)
    {
        if (Path.IsPathRooted(relative)) throw new ArgumentException("Expected a path relative to the project directory.");
        var path = Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.Equals(Root, _pathComparison) && !path.StartsWith(Root + Path.DirectorySeparatorChar, _pathComparison))
            throw new ArgumentException($"Path '{relative}' escapes the project directory.");
        var current = Root;
        foreach (var component in Path.GetRelativePath(Root, path).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Project path '{relative}' traverses a link.");
        }
        return path;
    }

    public JsonElement? Read(string relative, IList<ToolDiagnostic> diagnostics)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Resolve(relative)));
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            diagnostics.Add(new("json.invalid", "error", exception.Message, relative,
                JsonPointer: exception.Path, Line: exception.LineNumber, Column: exception.BytePositionInLine));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        { diagnostics.Add(new("file.read", "error", exception.Message, relative)); }
        return null;
    }

    public IReadOnlyList<string> Files(string? relativeRoot, IList<ToolDiagnostic> diagnostics)
    {
        var start = Resolve(relativeRoot ?? ".");
        if (!Directory.Exists(start)) throw new DirectoryNotFoundException($"Directory not found: '{start}'.");
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(start);
        while (pending.TryPop(out var directory))
        {
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(directory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { diagnostics.Add(new("directory.read", "error", exception.Message, Relative(directory))); continue; }
            foreach (var entry in entries)
            {
                var relative = Relative(entry);
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { diagnostics.Add(new("file.inspect", "error", exception.Message, relative)); continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                { diagnostics.Add(new("file.linkSkipped", "warning", "Skipped a filesystem link.", relative)); continue; }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    var name = Path.GetFileName(entry);
                    if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("obj", StringComparison.OrdinalIgnoreCase)) continue;
                    if (relativeRoot is null && directory.Equals(Root, _pathComparison) && ExcludedRoots.Contains(name)) continue;
                    pending.Push(entry);
                }
                else files.Add(relative);
            }
        }
        return files.Order(StringComparer.Ordinal).ToArray();
    }

    public static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    public static IReadOnlyList<JsonElement> Array(JsonElement? value, string name, string file, IList<ToolDiagnostic> diagnostics)
    {
        if (value is null) return [];
        if (value.Value.ValueKind == JsonValueKind.Object && value.Value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Array)
            return property.EnumerateArray().ToArray();
        diagnostics.Add(new("json.arrayRequired", "error", $"'{name}' must be an array.", file, JsonPointer: "/" + name));
        return [];
    }
}
