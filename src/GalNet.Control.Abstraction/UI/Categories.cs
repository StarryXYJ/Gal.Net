namespace GalNet.Control.Abstraction.UI;

/// <summary>Marker behaviour contracts implemented by plugin widget and screen categories.</summary>
public interface IWidgetCategory { string CategoryKey { get; } }
public interface IScreenCategory { string CategoryKey { get; } }

public abstract class WidgetCategory(string categoryKey) : IWidgetCategory
{
    public string CategoryKey { get; } = categoryKey;
}

public abstract class ScreenCategory(string categoryKey) : IScreenCategory
{
    public string CategoryKey { get; } = categoryKey;
}
