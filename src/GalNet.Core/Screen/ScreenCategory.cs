namespace GalNet.Core.Screen;

/// <summary>
/// 页面大类（Title, Settings, SaveLoad, Gallery, Game）。
/// </summary>
public sealed class ScreenCategory
{
    public string Name { get; }

    public ScreenCategory(string name) => Name = name;

    public override string ToString() => Name;

    public override bool Equals(object? obj) =>
        obj is ScreenCategory other && Name == other.Name;

    public override int GetHashCode() => Name.GetHashCode();

    // ── 内置类别 ──
    public static readonly ScreenCategory Title = new("Title");
    public static readonly ScreenCategory Settings = new("Settings");
    public static readonly ScreenCategory SaveLoad = new("SaveLoad");
    public static readonly ScreenCategory Gallery = new("Gallery");
    public static readonly ScreenCategory Game = new("Game");
    public static readonly ScreenCategory Backlog = new("Backlog");
}
