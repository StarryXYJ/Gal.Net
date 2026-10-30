using System.Reflection;

namespace GalNet.Storage.FileSystem;

/// <summary>Compiled host policy. Binding identifies a game project, so that game's content can be updated.</summary>
public sealed record GameLaunchPolicy(string Mode, string? BoundProjectId = null)
{
    public bool AllowsExternalContent => Mode != "Bound";
    public bool AllowsDebugging => Mode == "Developer";

    public static GameLaunchPolicy FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);
        return new(metadata.GetValueOrDefault("GalNetPlayerMode") ?? "Player", metadata.GetValueOrDefault("GalNetBoundProjectId"));
    }

    public void ValidateContent(string? contentDirectory)
    {
        if (Mode is not ("Developer" or "Player" or "Bound")) throw new InvalidDataException("Unknown player launch mode.");
        if (Mode != "Bound") return;
        if (string.IsNullOrWhiteSpace(BoundProjectId) || string.IsNullOrWhiteSpace(contentDirectory))
            throw new InvalidDataException("A bound player requires its compiled game identity and game content.");
        if (!string.Equals(BoundProjectId, InstalledGameDescriptor.Read(contentDirectory).ProjectId, StringComparison.Ordinal))
            throw new InvalidDataException("This player is bound to a different game project.");
    }
}
