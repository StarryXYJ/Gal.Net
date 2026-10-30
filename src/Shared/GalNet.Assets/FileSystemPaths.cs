namespace GalNet.Assets;

/// <summary>Physical filesystem checks shared by authoring, local assets and package I/O.</summary>
public static class FileSystemPaths
{
    public static bool IsSameOrChild(string path, string parent)
    {
        var relative = Path.GetRelativePath(parent, path);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>Requires a slash-separated relative file name, without dot or empty segments.</summary>
    public static void ValidateRelativeFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') ||
            path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException($"Path '{path}' is not a safe relative file path.");
    }

    public static string ResolveRelativeFile(string root, string relative)
    {
        ValidateRelativeFile(relative);
        var destination = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsSameOrChild(destination, root))
            throw new InvalidDataException($"Path '{relative}' escapes its destination directory.");
        return destination;
    }

    /// <summary>Checks an existing path or its existing ancestors; does not create or resolve links.</summary>
    public static void RequireNoLinks(string path, string message)
    {
        var fullPath = Path.GetFullPath(path);
        var current = Path.GetPathRoot(fullPath)!;
        foreach (var component in fullPath[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"{message} Path: '{current}'.");
        }
    }
}
