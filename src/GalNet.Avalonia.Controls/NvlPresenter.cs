using Avalonia.Controls;

namespace GalNet.Game.Controls;

/// <summary>Scrollable NVL-mode dialogue history. Its items and presentation remain host-owned.</summary>
public class NvlPresenter : ItemsControl
{
}

/// <summary>Default item shape for <see cref="NvlPresenter"/>; applications may use their own item template instead.</summary>
public sealed record NvlLine(string? Speaker, string Text);
