namespace GalNet.Editor.Abstraction.Commands;

public sealed record ProjectBuildReport(string OutputPath, int CompiledGroupCount);
public sealed record ProjectExportReport(string? PackagePath);
public sealed record ProjectExportDirectoryReport(string ContentDirectory);
public sealed record GalpakInstallReport(string InstallationDirectory);
