using System.Diagnostics;
using System.Text.RegularExpressions;
using GalNet.Assets;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Storage.FileSystem;

namespace GalNet.Editor.Shared.Services;

public sealed record BuildComposeRequest(string AppProject, string Content, string Output, string RuntimeIdentifier,
    string EntryPoint, string Version, string Configuration = "Release", string SampleFlavor = "Player",
    string? PublishProfile = null, string? SdkVersion = null, string? MiniaudioNativePath = null, string? MediaDirectory = null);
public sealed record BuildProcessResult(int ExitCode, string OutputTail, string ErrorTail);
public sealed record BuildComposeStep(string Name, int? ExitCode, string StandardOutputLog, string StandardErrorLog);
public sealed record BuildComposeReport(string? SdkVersion, string LogDirectory, IReadOnlyList<BuildComposeStep> Steps,
    GameDistributionResult? Distribution);

public interface IBuildComposeProcessRunner
{
    Task<BuildProcessResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments,
        string stdoutLog, string stderrLog, CancellationToken cancellationToken);
}

/// <summary>Builds only explicitly selected application projects; content compilation stays a separate authoring operation.</summary>
public sealed class BuildComposeService(IBuildComposeProcessRunner? runner = null)
{
    private readonly IBuildComposeProcessRunner _runner = runner ?? new BuildComposeProcessRunner();

    public async Task<ToolResult> BuildAsync(BuildComposeRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var project = Path.GetFullPath(request.AppProject);
        var content = Path.GetFullPath(request.Content);
        var target = Path.GetFullPath(request.Output);
        if (!project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || !File.Exists(project))
            throw new ArgumentException("Select an existing application .csproj.");
        if (request.RuntimeIdentifier is not ("win-x64" or "linux-x64")) throw new ArgumentException("Only win-x64/linux-x64 builds are supported.");
        var linux = request.RuntimeIdentifier == "linux-x64";
        if (!target.EndsWith(linux ? ".tar.gz" : ".zip", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Output extension must match the target RID.");
        foreach (var value in new[] { request.Configuration, request.Version, request.SdkVersion, request.PublishProfile })
            if (value is not null && !Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_.-]*\z")) throw new ArgumentException("Invalid build configuration, version, SDK or profile.");
        if (request.SampleFlavor is not ("Player" or "Developer")) throw new ArgumentException("Select Player or Developer.");
        if (request.PublishProfile == "WindowsReadyToRun" && linux) throw new ArgumentException("WindowsReadyToRun is a win-x64 profile; use Desktop for Linux.");
        FileSystemPaths.ValidateRelativeFile(request.EntryPoint);
        if (request.EntryPoint.Contains('/') || request.EntryPoint.Any(char.IsControl)) throw new ArgumentException("Entry point must be a published apphost filename.");
        if (!Directory.Exists(content) && !File.Exists(content)) throw new FileNotFoundException("Content input was not found.", content);
        if (linux && request.MediaDirectory is null) throw new ArgumentException("Linux builds require a prepared private media directory.");
        if (!linux && request.MediaDirectory is not null) throw new ArgumentException("Media directory is Linux-only.");
        if (request.PublishProfile is { } profile && !File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "Properties/PublishProfiles", profile + ".pubxml")))
            throw new ArgumentException("The selected publish profile does not exist in the application project.");
        foreach (var input in new[] { project, content, request.MediaDirectory, request.MiniaudioNativePath })
        {
            if (input is null) continue;
            var path = Path.GetFullPath(input);
            FileSystemPaths.RequireNoLinks(path, "Build inputs cannot traverse links.");
            if (FileSystemPaths.IsSameOrChild(target, path)) throw new InvalidDataException("Output must be outside the inputs.");
        }
        if (request.MediaDirectory is { } media && !Directory.Exists(media)) throw new DirectoryNotFoundException("Media directory was not found.");
        if (request.MiniaudioNativePath is { } native && !File.Exists(native)) throw new FileNotFoundException("Native audio library was not found.", native);
        FileSystemPaths.RequireNoLinks(target, "Build output cannot traverse links.");
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("Build output already exists.");
        var parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        var id = Guid.NewGuid().ToString("N");
        var stage = Path.Combine(parent, ".build-compose-" + id);
        var logs = target + ".build-logs-" + id;
        Directory.CreateDirectory(logs);
        var steps = new List<BuildComposeStep>();
        string? sdk = null;
        ToolResult Fail(string code, string message) => new("distribution.build-compose", false,
            new BuildComposeReport(sdk, logs, steps.ToArray(), null), [new(code, "error", message)]);
        async Task<BuildProcessResult> Run(string name, IReadOnlyList<string> arguments)
        {
            var stdout = Path.Combine(logs, name + "-stdout.log");
            var stderr = Path.Combine(logs, name + "-stderr.log");
            var index = steps.Count;
            steps.Add(new(name, null, stdout, stderr));
            var result = await _runner.RunAsync(Path.GetDirectoryName(project)!, arguments, stdout, stderr, cancellationToken);
            steps[index] = new(name, result.ExitCode, stdout, stderr);
            return result;
        }
        try
        {
            BuildProcessResult sdkResult;
            try { sdkResult = await Run("sdk", ["--version"]); }
            catch (System.ComponentModel.Win32Exception error) { return Fail("build.sdkUnavailable", error.Message); }
            if (sdkResult.ExitCode != 0) return Fail("build.sdkUnavailable", $"dotnet --version failed ({sdkResult.ExitCode}). See build logs.");
            sdk = sdkResult.OutputTail.Trim().Split('\n').Last().Trim();
            if (!Regex.IsMatch(sdk, @"\A\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?\z")) return Fail("build.sdkUnavailable", "dotnet did not report an SDK version.");
            if (request.SdkVersion is { } expected && sdk != expected) return Fail("build.sdkMismatch", $"Expected SDK {expected}, resolved {sdk}.");
            var publish = Path.Combine(stage, "publish");
            var args = new List<string> { "publish", project, "-c", request.Configuration, "-r", request.RuntimeIdentifier,
                "--self-contained", "true", "-o", publish, "-p:UseAppHost=true", "-p:PublishSingleFile=false", "-p:PublishTrimmed=false",
                "-p:PublishAot=false", "-p:PublishReadyToRun=false", "-p:RestoreLockedMode=true", "-p:NuGetAudit=false",
                "-p:UseSharedCompilation=false", "-m:1", "-nr:false", "-p:Version=" + request.Version,
                "-p:EnableGameDebugging=" + (request.SampleFlavor == "Developer" ? "true" : "false"),
                "-p:EnableLaunchArguments=" + (request.SampleFlavor == "Developer" ? "true" : "false") };
            if (request.PublishProfile is { } selected) args.Add("-p:PublishProfile=" + selected);
            if (request.PublishProfile == "Desktop") args.Add("-p:GalNetDesktopPublishTarget=" + request.RuntimeIdentifier);
            if (request.MiniaudioNativePath is { } library) args.Add("-p:GalNetMiniaudioNativePath=" + EscapeMsBuild(Path.GetFullPath(library)));
            // Restore separately: publish's implicit restore can clear the RID for
            // portable analyzer references even though the selected profile lock is RID-specific.
            var restoreArgs = new List<string> { "restore", project,
                "-p:RuntimeIdentifier=" + request.RuntimeIdentifier, "-p:Configuration=" + request.Configuration,
                "-p:SelfContained=true", "-m:1", "-nr:false" };
            restoreArgs.AddRange(args.Where(argument => argument.StartsWith("-p:", StringComparison.Ordinal)));
            // Preserve the established Windows profile's locked RID graph (as in
            // Publish.ps1). Desktop's portable analyzer lock must not inherit the
            // plural RuntimeIdentifiers property introduced by restore -r.
            if (request.PublishProfile == "WindowsReadyToRun") restoreArgs.AddRange(["-r", request.RuntimeIdentifier]);
            var restored = await Run("restore", restoreArgs);
            if (restored.ExitCode != 0) return Fail("build.restoreFailed", $"dotnet restore failed ({restored.ExitCode}). See build logs.");
            args.Add("--no-restore");
            var built = await Run("publish", args);
            if (built.ExitCode != 0) return Fail("build.publishFailed", $"dotnet publish failed ({built.ExitCode}). See build logs.");
            var runtime = Path.Combine(stage, linux ? "runtime.tar.gz" : "runtime.zip");
            await GameDistributionComposer.PreparePublishedRuntimeAsync(publish, runtime,
                new(request.RuntimeIdentifier, request.EntryPoint, request.Version, request.SampleFlavor, request.MediaDirectory), cancellationToken);
            var distribution = await GameDistributionComposer.ComposeAsync(runtime, content, target, cancellationToken);
            return new("distribution.build-compose", true, new BuildComposeReport(sdk, logs, steps.ToArray(), distribution), []);
        }
        catch (OperationCanceledException error)
        {
            error.Data["buildReport"] = new BuildComposeReport(sdk, logs, steps.ToArray(), null);
            throw;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException
            or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return Fail("build.failed", error.Message); }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
    }

    private static string EscapeMsBuild(string value) => string.Concat(value.Select(character => "%*?@$();,'".Contains(character)
        ? "%" + ((int)character).ToString("X2", System.Globalization.CultureInfo.InvariantCulture) : character.ToString()));
}

public sealed class BuildComposeProcessRunner : IBuildComposeProcessRunner
{
    public async Task<BuildProcessResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments,
        string stdoutLog, string stderrLog, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        using var process = new Process { StartInfo = start };
        using var stdout = new StreamWriter(stdoutLog);
        using var stderr = new StreamWriter(stderrLog);
        process.Start();
        static async Task<string> Drain(StreamReader reader, StreamWriter writer)
        {
            var tail = "";
            while (await reader.ReadLineAsync() is { } line)
            {
                await writer.WriteLineAsync(line);
                tail += line + "\n";
                if (tail.Length > 8192) tail = tail[^8192..];
            }
            return tail;
        }
        var output = Drain(process.StandardOutput, stdout);
        var errors = Drain(process.StandardError, stderr);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, errors);
            throw;
        }
        return new(process.ExitCode, await output, await errors);
    }
}
