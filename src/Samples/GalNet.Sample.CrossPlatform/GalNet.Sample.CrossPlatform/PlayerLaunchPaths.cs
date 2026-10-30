namespace GalNet.Sample.CrossPlatform;

/// <summary>Platform-owned content and writable locations, resolved before opening a player session.</summary>
public sealed record PlayerLaunchPaths(string ContentDirectory, string ProfileDirectory, string? ScreenshotDirectory = null);
