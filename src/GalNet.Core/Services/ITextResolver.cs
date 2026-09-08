namespace GalNet.Core.Services;

/// <summary>Host-provided localization boundary used by runtime text and choice resolution.</summary>
public interface ITextResolver
{
    string Resolve(string key);
}

public sealed class PassthroughTextResolver : ITextResolver
{
    public static PassthroughTextResolver Instance { get; } = new();
    private PassthroughTextResolver() { }
    public string Resolve(string key) => key;
}
