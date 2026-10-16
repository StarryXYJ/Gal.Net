namespace GalNet.Editor.Abstraction.Services;

/// <summary>Stable operation code and parameters. Hosts choose how to present them; Message remains diagnostic detail.</summary>
public sealed class EditorOperationException(string code, string message, params object[] arguments) : IOException(message)
{
    public string Code { get; } = code;
    public object[] Arguments { get; } = arguments;
}
