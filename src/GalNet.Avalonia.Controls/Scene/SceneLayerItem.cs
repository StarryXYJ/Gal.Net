using Avalonia.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GalNet.Core.Scene;

namespace GalNet.Game.Controls.Scene;

/// <summary>Bindable Layer render data passed from the shared game page to <see cref="SceneLayerHost"/>.</summary>
public sealed class SceneLayerItem : INotifyPropertyChanged
{
    private string _handleId = string.Empty;
    private IImage? _image;
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
    private double _blindsProgress = 1;
    private int _blindsBladeCount;
    private bool _blindsHorizontal;
    private FlipbookDefinition? _flipbook;

    public string HandleId { get => _handleId; set => SetField(ref _handleId, value); }
    public IImage? Image { get => _image; set => SetField(ref _image, value); }
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
    /// <summary>Optional sprite-sheet layout for <see cref="Image"/>. The source remains one image while index selects its current frame.</summary>
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
    /// <summary>0 hides a blinds-masked layer and 1 reveals it fully.</summary>
    public double BlindsProgress { get => _blindsProgress; set => SetField(ref _blindsProgress, Math.Clamp(value, 0, 1)); }
    /// <summary>Zero means no blinds mask.</summary>
    public int BlindsBladeCount { get => _blindsBladeCount; set => SetField(ref _blindsBladeCount, Math.Max(0, value)); }
    public bool BlindsHorizontal { get => _blindsHorizontal; set => SetField(ref _blindsHorizontal, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
