using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace GalNet.Control;

/// <summary>
/// Flipbook 精灵表动画辅助类。
/// 将一张精灵表按行列切分为帧，按指定帧率循环播放。
/// 支持方向控制 (forward/backward/pingpong)。
/// </summary>
public sealed class FlipbookHelper : IDisposable
{
    private readonly Image _target;
    private readonly int _cols;
    private readonly int _totalFrames;
    private readonly TimeSpan _frameInterval;
    private readonly string _direction;   // forward / backward / pingpong
    private readonly double _frameWidth;
    private readonly double _frameHeight;

    private int _currentFrame;
    private bool _forward = true;
    private DispatcherTimer? _timer;
    private bool _disposed;

    /// <summary>
    /// 创建 Flipbook 动画辅助。
    /// </summary>
    /// <param name="target">目标 Image 控件（应已设置 Source 为精灵表图片）</param>
    /// <param name="rows">精灵表行数</param>
    /// <param name="cols">精灵表列数</param>
    /// <param name="frameRate">帧率 (fps)，默认 12</param>
    /// <param name="direction">播放方向: "forward" / "backward" / "pingpong"</param>
    public FlipbookHelper(Image target, int rows, int cols,
        double frameRate = 12,
        string direction = "forward")
    {
        _target = target;
        _cols = Math.Max(1, cols);
        _totalFrames = Math.Max(1, rows) * _cols;
        _frameInterval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, frameRate));
        _direction = direction.ToLowerInvariant();

        if (target.Source is Bitmap bitmap)
        {
            _frameWidth = bitmap.Size.Width / _cols;
            _frameHeight = bitmap.Size.Height / Math.Max(1, rows);

            target.Width = _frameWidth;
            target.Height = _frameHeight;
            target.ClipToBounds = true;
            target.Stretch = Stretch.None;
            target.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            target.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        }
        else
        {
            _frameWidth = 64;
            _frameHeight = 64;
        }

        UpdateFrame();
        StartTimer();
    }

    /// <summary>帧率 (fps)</summary>
    public double FrameRate => 1000.0 / _frameInterval.TotalMilliseconds;

    /// <summary>总帧数</summary>
    public int TotalFrames => _totalFrames;

    /// <summary>当前帧索引 (0-based)</summary>
    public int CurrentFrame => _currentFrame;

    private void StartTimer()
    {
        _timer = new DispatcherTimer { Interval = _frameInterval };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_disposed) return;
        AdvanceFrame();
        UpdateFrame();
    }

    private void AdvanceFrame()
    {
        switch (_direction)
        {
            case "backward":
                _currentFrame--;
                if (_currentFrame < 0)
                    _currentFrame = _totalFrames - 1;
                break;

            case "pingpong":
                if (_forward)
                {
                    _currentFrame++;
                    if (_currentFrame >= _totalFrames - 1)
                        _forward = false;
                }
                else
                {
                    _currentFrame--;
                    if (_currentFrame <= 0)
                        _forward = true;
                }
                break;

            default: // forward
                _currentFrame = (_currentFrame + 1) % _totalFrames;
                break;
        }
    }

    private void UpdateFrame()
    {
        if (_disposed) return;

        var col = _currentFrame % _cols;
        var row = _currentFrame / _cols;

        _target.Margin = new Thickness(-(col * _frameWidth), -(row * _frameHeight), 0, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Stop();
        _timer = null;
    }
}
