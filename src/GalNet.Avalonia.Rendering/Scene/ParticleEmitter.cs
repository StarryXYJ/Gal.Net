using System.ComponentModel;
using System.Runtime.CompilerServices;
using GalNet.Core.Scene;
using SkiaSharp;

namespace GalNet.Rendering.Scene;

/// <summary>Renderer-owned particle batch. It never creates Avalonia controls or per-particle render targets.</summary>
public sealed class ParticleEmitter : IFrameUpdatableSceneRenderable, INotifyPropertyChanged, IDisposable
{
    private sealed class Particle { public float X; public float Y; public float Vx; public float Vy; public float Age; public float Life; public float Scale; }
    private readonly List<Particle> _active = [];
    private readonly Stack<Particle> _pool = [];
    private readonly SceneTexture? _texture;
    private readonly Random _random;
    private readonly int _maximum;
    private readonly float _gravityX;
    private readonly float _gravityY;
    private readonly IReadOnlyList<ParticleCurveKey> _sizeCurve;
    private readonly IReadOnlyList<ParticleColorCurveKey> _colorCurve;
    private SKRect[] _sprites = [];
    private SKRotationScaleMatrix[] _transforms = [];
    private SKColor[] _colors = [];
    private int _lastRenderedParticleCount;
    private float _rate;
    private float _speedX;
    private float _speedY;
    private float _noise;
    private float _scale;
    private float _life;
    private float _emissionCarry;
    private bool _stopping;
    private bool _drained;
    private double _z;

    public ParticleEmitter(string handleId, SceneTexture? texture, ParticleEmitterDefinition definition, float z)
    {
        HandleId = handleId; _texture = texture; _maximum = definition.MaxParticles; _random = new Random(definition.Seed);
        _gravityX = definition.GravityX; _gravityY = definition.GravityY; _z = z;
        _sizeCurve = definition.SizeCurve?.OrderBy(key => key.Time).ToArray() ?? [];
        _colorCurve = definition.ColorCurve?.OrderBy(key => key.Time).ToArray() ?? [];
        EmissionRate = definition.EmissionRate; InitialVelocityX = definition.InitialVelocityX; InitialVelocityY = definition.InitialVelocityY;
        Noise = definition.Noise; ParticleScale = definition.ParticleScale; ParticleLifetime = definition.ParticleLifetime;
    }

    public string HandleId { get; }
    public double Z { get => _z; set => SetField(ref _z, value); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<ParticleEmitter>? Drained;
    public float EmissionRate { get => _rate; set => _rate = Math.Max(0, value); }
    public float InitialVelocityX { get => _speedX; set => _speedX = value; }
    public float InitialVelocityY { get => _speedY; set => _speedY = value; }
    public float Noise { get => _noise; set => _noise = Math.Max(0, value); }
    public float ParticleScale { get => _scale; set => _scale = Math.Max(.001f, value); }
    public float ParticleLifetime { get => _life; set => _life = Math.Max(.01f, value); }

    /// <summary>Stops spawning but keeps existing flakes visible until their normal lifetime ends.</summary>
    public void StopEmission() { _stopping = true; EmissionRate = 0; }

    public bool Update(SceneFrameContext frame)
    {
        var delta = (float)frame.Delta.TotalSeconds;
        if (!_stopping)
        {
            _emissionCarry += _rate * delta;
            while (_emissionCarry >= 1 && _active.Count < _maximum) { _emissionCarry--; Spawn(frame.Size); }
        }
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            var particle = _active[index];
            particle.Age += delta;
            // Preserve the established snow motion: noise perturbs horizontal drift only.
            // Gravity is optional authoring data and defaults to zero for existing content.
            particle.Vx += (_gravityX + ((float)_random.NextDouble() - .5f) * _noise) * delta;
            particle.Vy += _gravityY * delta;
            particle.X += particle.Vx * delta; particle.Y += particle.Vy * delta;
            if (particle.Age < particle.Life) continue;
            _active.RemoveAt(index); _pool.Push(particle);
        }
        if (_stopping && _active.Count == 0)
        {
            if (!_drained) { _drained = true; Drained?.Invoke(this); }
            return false;
        }
        return !_stopping || _active.Count > 0;
    }

    public void Render(SceneRenderContext context)
    {
        if (_active.Count == 0)
        {
            if (_lastRenderedParticleCount > 0) Array.Clear(_colors, 0, _lastRenderedParticleCount);
            _lastRenderedParticleCount = 0;
            return;
        }
        var image = _texture?.SkImage;
        if (image is null) { RenderFallback(context); return; }

        // One atlas submission batches all living sprites into the active CPU or GPU Skia canvas.
        EnsureAtlasCapacity(_active.Count);
        if (_lastRenderedParticleCount > _active.Count)
            Array.Clear(_colors, _active.Count, _lastRenderedParticleCount - _active.Count);
        var source = new SKRect(0, 0, image.Width, image.Height);
        for (var index = 0; index < _active.Count; index++)
        {
            var particle = _active[index];
            var progress = Math.Clamp(particle.Age / particle.Life, 0, 1);
            var size = 28f * particle.Scale * SampleSize(progress);
            var scale = size / Math.Max(1, image.Width);
            _sprites[index] = source;
            _transforms[index] = new SKRotationScaleMatrix(scale, 0, particle.X - size / 2, particle.Y - size / 2);
            var color = SampleColor(progress);
            _colors[index] = color.WithAlpha((byte)Math.Clamp(Math.Round(color.Alpha * (1 - progress)), 0, 255));
        }
        using var paint = new SKPaint { IsAntialias = true };
        // DrawAtlas in the pinned SkiaSharp version accepts arrays rather than spans. Unused
        // cache entries keep their zero-sized source and transparent color, so they are inert.
        context.Canvas.DrawAtlas(image, _sprites, _transforms, _colors, SKBlendMode.Modulate, paint);
        _lastRenderedParticleCount = _active.Count;
    }

    private void Spawn(SKSize size)
    {
        var particle = _pool.TryPop(out var reused) ? reused : new Particle();
        particle.X = (float)_random.NextDouble() * Math.Max(1, size.Width); particle.Y = -12;
        particle.Vx = _speedX + ((float)_random.NextDouble() - .5f) * _noise;
        particle.Vy = _speedY + ((float)_random.NextDouble() - .5f) * _noise;
        particle.Age = 0; particle.Life = _life * (.75f + (float)_random.NextDouble() * .5f); particle.Scale = _scale * (.75f + (float)_random.NextDouble() * .5f);
        _active.Add(particle);
    }

    private void RenderFallback(SceneRenderContext context)
    {
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        foreach (var particle in _active)
        {
            var progress = Math.Clamp(particle.Age / particle.Life, 0, 1);
            var color = SampleColor(progress);
            paint.Color = color.WithAlpha((byte)Math.Clamp(Math.Round(color.Alpha * (1 - progress)), 0, 255));
            var size = 28f * particle.Scale * SampleSize(progress);
            context.Canvas.DrawCircle(particle.X, particle.Y, size / 2, paint);
        }
    }

    public void Dispose() { _active.Clear(); _pool.Clear(); _sprites = []; _transforms = []; _colors = []; _lastRenderedParticleCount = 0; Drained = null; }
    private void EnsureAtlasCapacity(int count)
    {
        if (_sprites.Length >= count) return;
        var capacity = Math.Max(count, Math.Max(16, _sprites.Length * 2));
        Array.Resize(ref _sprites, capacity);
        Array.Resize(ref _transforms, capacity);
        Array.Resize(ref _colors, capacity);
    }
    private float SampleSize(float progress) => Sample(_sizeCurve, progress, 1, static key => key.Value);
    private SKColor SampleColor(float progress)
    {
        if (_colorCurve.Count == 0) return SKColors.White;
        var color = Sample(_colorCurve, progress, _colorCurve[0].Color, static key => key.Color);
        return TryParseColor(color, out var parsed) ? parsed : SKColors.White;
    }
    private static T Sample<T, TKey>(IReadOnlyList<TKey> curve, float progress, T fallback, Func<TKey, T> value) where TKey : notnull
    {
        if (curve.Count == 0) return fallback;
        // Curves intentionally use stepped keys for color and scalar values; interpolation can be
        // introduced later without changing the platform-neutral authoring shape.
        var key = curve.LastOrDefault(key => GetTime(key) <= progress) ?? curve[0];
        return value(key);
    }
    private static float GetTime<TKey>(TKey key) => key switch { ParticleCurveKey size => size.Time, ParticleColorCurveKey color => color.Time, _ => 0 };
    private static bool TryParseColor(string value, out SKColor color)
    {
        color = SKColors.White;
        if (string.IsNullOrWhiteSpace(value) || value[0] != '#') return false;
        var hex = value[1..];
        if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb)) { color = new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); return true; }
        if (hex.Length == 8 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgba)) { color = new SKColor((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba); return true; }
        return false;
    }
    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
