using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace GalNet.Debugging;

public sealed record DebugIdentity(string ProjectId, string ContentFingerprint, string? ContractFingerprint = null);
public sealed record DebugInvitation(int Port, string Token, string SessionId, DebugIdentity Identity);
public sealed record DebugRunInfo(string RunId, long Generation, string[] Capabilities, long? OperationGeneration = null);
public sealed record DebugHostStatus(string SessionId, bool CanStart, DebugRunInfo? Run);
public sealed record DebugCommand(string Method, string? RunId, long? Generation, JsonElement Arguments);
public interface IDebugGameRun
{
    DebugRunInfo Info { get; }
    Task<object?> ExecuteAsync(DebugCommand command, CancellationToken cancellationToken);
    Task OnDisconnectedAsync() => Task.CompletedTask;
}

internal sealed record DebugMessage(string Kind, string Id, string? Method, JsonElement Payload, string? Error = null);
internal sealed record DebugHello(int Version, string Token, string SessionId, DebugIdentity Identity);

internal static class DebugWire
{
    internal const int MaxFrameLength = 4 * 1024 * 1024;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static JsonElement Element(object? value) => JsonSerializer.SerializeToElement(value, Json);
    internal static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length > MaxFrameLength) throw new InvalidDataException("Debug message exceeds the frame limit.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
    }
    internal static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var size = BinaryPrimitives.ReadInt32BigEndian(header);
        if (size is <= 0 or > MaxFrameLength) throw new InvalidDataException("Invalid debug frame length.");
        var bytes = new byte[size];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("Empty debug frame.");
    }
}

/// <summary>Computes the identity of a built test content directory, independently in each process.</summary>
public static class DebugContentIdentity
{
    public static string Compute(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var root = Path.GetFullPath(directory);
        var files = new List<string>();
        foreach (var folder in new[] { "Graph", "I18n", "Contracts" })
        {
            var path = Path.Combine(root, folder);
            if (Directory.Exists(path)) files.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories));
        }
        var settings = Path.Combine(root, "settings.json");
        if (File.Exists(settings)) files.Add(settings);
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace('\\', '/')));
            hash.AppendData([0]);
            using var input = File.OpenRead(file);
            var buffer = new byte[65536];
            int count;
            while ((count = input.Read(buffer)) > 0) hash.AppendData(buffer.AsSpan(0, count));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
