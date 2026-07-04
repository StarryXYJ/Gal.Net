namespace GalNet.Core.Variable;

/// <summary>
/// 变量路由 —— 树状键路径解析（如 "player.affection.alice"）。
/// </summary>
public sealed class VariableRoute
{
    /// <summary>路由路径（点分隔）</summary>
    public string Path { get; }

    /// <summary>各路径段</summary>
    public IReadOnlyList<string> Segments { get; }

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
