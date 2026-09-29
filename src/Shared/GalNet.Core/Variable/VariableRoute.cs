namespace GalNet.Core.Variable;

/// <summary>Address of a variable or nested value using a dot-separated path.</summary>
/// <remarks><see cref="Segments"/> omits empty path segments, while <see cref="Path"/> preserves the original input for serialization.</remarks>
public sealed class VariableRoute
{
    /// <summary>路由路径（点分隔）</summary>
    public string Path { get; }

    /// <summary>各路径段</summary>
    public IReadOnlyList<string> Segments { get; }

    /// <param name="path">Original dot-separated route.</param>
    public VariableRoute(string path)
    {
        Path = path;
        Segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
    }

    public override string ToString() => Path;

    public static implicit operator VariableRoute(string path) => new(path);
    public static implicit operator string(VariableRoute route) => route.Path;

    public override bool Equals(object? obj) =>
        obj is VariableRoute other && Path == other.Path;

    public override int GetHashCode() => Path.GetHashCode();
}
