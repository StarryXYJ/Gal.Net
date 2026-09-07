using Avalonia.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GalNet.Game.Controls;

/// <summary>ItemsControl host for application-provided visual scene layers.</summary>
public class SceneLayerHost : ItemsControl
{
}

/// <summary>Minimal bindable layer item used by the default <see cref="SceneLayerHost"/> template.</summary>
public sealed class SceneLayerItem : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private Control? _content;
    private double _x;
    private double _y;
    private int _zIndex;
    private bool _isVisible = true;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public Control? Content { get => _content; set => SetField(ref _content, value); }
    public double X { get => _x; set => SetField(ref _x, value); }
    public double Y { get => _y; set => SetField(ref _y, value); }
    public int ZIndex { get => _zIndex; set => SetField(ref _zIndex, value); }
    public bool IsVisible { get => _isVisible; set => SetField(ref _isVisible, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
