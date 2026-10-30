using System.Security.Cryptography;
using GalNet.Debugging;

namespace GalNet.Avalonia.Debugging;

/// <summary>Validates a desktop launch invitation against the application's actual contract file.</summary>
public static class DebugLaunchArguments
{
    public static DebugInvitation? Parse(IReadOnlyDictionary<string, string> arguments,
        string contentDirectory, string contractAssemblyPath)
    {
        if (arguments.Count == 0) return null;
        if (!arguments.TryGetValue("--debug-port", out var portText) || !int.TryParse(portText, out var port) || port is <= 0 or > 65535 ||
            !arguments.TryGetValue("--debug-token", out var token) || !arguments.TryGetValue("--debug-session", out var session) ||
            !arguments.TryGetValue("--debug-project", out var project))
            throw new ArgumentException("Debug launch requires a content directory, port, token, session and project identity.");
        string? fingerprint = null;
        if (arguments.TryGetValue("--debug-contract", out var expected))
        {
            fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(contractAssemblyPath)));
            if (!string.Equals(expected, fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The App contract differs from the Editor project. Expected SHA256: {expected}; actual SHA256: {fingerprint}. Deploy the App's matching Contracts and reopen the Editor project before debugging.");
        }
        return new(port, token, session, new(project, DebugContentIdentity.Compute(contentDirectory), fingerprint));
    }
}
