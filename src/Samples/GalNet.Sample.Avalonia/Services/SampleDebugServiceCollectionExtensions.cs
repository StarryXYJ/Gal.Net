using GalNet.Sample.Avalonia.Services;
using GalNet.Avalonia.Debugging;
using GalNet.Runtime.Engine;
using GalNet.Project.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Sample.Avalonia.Debug;

/// <summary>Registers the optional in-app debug controls for the Avalonia sample.</summary>
internal static class SampleDebugConfiguration
{
    public static void Configure(IServiceCollection services)
    {
        var instructions = GameDebugHostOptions.CreateBuiltinReplayPolicy();
        instructions[ScreenshotSchema.TypeId] = DebugReplayBehavior.Ignore;
        services.AddGameDebugTools(new()
        {
            RestartAsync = _ => { SampleApplicationRestarter.Restart(); return Task.CompletedTask; },
            OpenLogDirectory = () => new SampleDebugLogDirectory().Open(),
            Instructions = instructions
        });
    }
}
