using System.ComponentModel;
using System.Runtime.CompilerServices;
using GalNet.Core.Scene;

namespace GalNet.Rendering.Scene;

/// <summary>Bindable Layer render data consumed by the fixed scene pipeline.</summary>
public sealed class SceneLayerItem : INotifyPropertyChanged
{
    private string _handleId = string.Empty;
    private SceneTexture? _texture;
    private string? _color;
    private double _x;
    private double _y;
    private double _rotationDegrees;
    private double _scaleX = 1;
    private double _scaleY = 1;
    private double _z;
    private LayerDisplayMode _displayMode;
    private double _opacity = 1;
    private bool _isVisible = true;
    private FlipbookDefinition? _flipbook;

    public string HandleId { get => _handleId; set => SetField(ref _handleId, value); }
    public SceneTexture? Texture { get => _texture; set => SetField(ref _texture, value); }
    public string? Color { get => _color; set => SetField(ref _color, value); }
    public double X { get => _x; set => SetField(ref _x, value); }
    public double Y { get => _y; set => SetField(ref _y, value); }
    public double RotationDegrees { get => _rotationDegrees; set => SetField(ref _rotationDegrees, value); }
    public double ScaleX { get => _scaleX; set => SetField(ref _scaleX, value); }
    public double ScaleY { get => _scaleY; set => SetField(ref _scaleY, value); }
    public double Z { get => _z; set => SetField(ref _z, value); }
    public LayerDisplayMode DisplayMode { get => _displayMode; set => SetField(ref _displayMode, value); }
    public double Opacity { get => _opacity; set => SetField(ref _opacity, value); }
    public bool IsVisible { get => _isVisible; set => SetField(ref _isVisible, value); }
    public FlipbookDefinition? Flipbook { get => _flipbook; set => SetField(ref _flipbook, value); }
    public double FlipbookIndex
    {
        get => _flipbook?.Index ?? 0;
        set
        {
            if (_flipbook is null || Math.Abs(_flipbook.Index - value) < double.Epsilon) return;
            _flipbook.Index = (float)Math.Max(0, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlipbookIndex)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
