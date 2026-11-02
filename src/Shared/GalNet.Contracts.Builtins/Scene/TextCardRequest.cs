namespace GalNet.Contracts.Builtins.Scene;

public enum TextCardPosition { TopLeft, Left, BottomLeft, TopCenter, Center, BottomCenter, TopRight, Right, BottomRight }

/// <summary>Transient, localizable story text. Null hold duration selects the application's style default.</summary>
public sealed record TextCardRequest(string Text, TextCardPosition Position = TextCardPosition.BottomLeft,
    string StyleName = "default", float? HoldSeconds = null);
