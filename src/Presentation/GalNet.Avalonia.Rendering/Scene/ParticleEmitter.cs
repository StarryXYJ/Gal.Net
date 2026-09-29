using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using GalNet.Core.Scene;
using SkiaSharp;

namespace GalNet.Rendering.Scene;

/// <summary>Renderer-owned particle batch. It never creates Avalonia controls or per-particle render targets.</summary>
public sealed class ParticleEmitter : IFrameUpdatableSceneRenderable, INotifyPropertyChanged, IDisposable
{
    private sealed class Particle
    {
        public float X; public float Y; public float Vx; public float Vy; public float Age; public float Life;
        public float Scale; public float Rotation; public float AngularVelocity; public float FlipbookRate;
        public int StartFrame; public SKColor InitialColor;
    }

    private static readonly IReadOnlyList<ParticleCurveKey> DefaultOpacityCurve =
        [new ParticleCurveKey(0, 1), new ParticleCurveKey(1, 0)];
    private readonly List<Particle> _active = [];
    private readonly Stack<Particle> _pool = [];
    private readonly SceneTexture? _texture;
    private readonly Random _random;
    private readonly int _maximum;
    private readonly float _gravityX;
    private readonly float _gravityY;
    private readonly float _drag;
    private readonly float _radialVelocity;
    private readonly float _orbitRadiansPerSecond;
    private readonly ParticleAttractorDefinition? _attractor;
    private readonly ParticleShapeDefinition? _shape;
    private readonly ParticleFlipbookDefinition? _flipbook;
    private readonly ParticleColorRange _initialColor;
    private readonly ParticleFloatRange _rotation;
    private readonly ParticleFloatRange _angularVelocity;
    private readonly IReadOnlyList<ParticleCurveKey> _sizeCurve;
    private readonly IReadOnlyList<ParticleCurveKey> _opacityCurve;
    private readonly IReadOnlyList<ParticleCurveKey> _velocityCurve;
    private readonly IReadOnlyList<ParticleCurveKey> _rotationCurve;
    private readonly ParticleColorCurveKey[] _colorCurve;
    private SKRect[] _sprites = [];
    private SKRotationScaleMatrix[] _transforms = [];
    private SKColor[] _colors = [];
    private int _lastRenderedParticleCount;
    private float _rate;
    private ParticleFloatRange _velocityX;
    private ParticleFloatRange _velocityY;
    private float _noise;
    private ParticleFloatRange _scale;
    private ParticleFloatRange _life;
    private float _emissionCarry;
    private int _pendingBurstCount;
    private bool _stopping;
    private bool _drained;
    private double _z;

    public ParticleEmitter(string handleId, SceneTexture? texture, ParticleEmitterDefinition definition, float z, int initialBurstCount = 0)
    {
        var initial = definition.InitialModule;
        var motion = definition.MotionModule;
        var lifetime = definition.LifetimeModule;
        HandleId = handleId; _texture = texture; _maximum = definition.MaxParticles; _random = new Random(definition.Seed);
        _gravityX = motion.GravityX; _gravityY = motion.GravityY; _drag = motion.Drag; _radialVelocity = motion.RadialVelocity;
        _orbitRadiansPerSecond = motion.OrbitDegreesPerSecond * MathF.PI / 180f; _attractor = motion.Attractor; _z = z;
        _shape = definition.Shape; _flipbook = definition.Flipbook is { IsValid: true } ? definition.Flipbook : null;
        _initialColor = initial.Color; _rotation = initial.RotationDegrees; _angularVelocity = initial.AngularVelocityDegrees;
        _pendingBurstCount = Math.Max(0, initialBurstCount);
        _sizeCurve = Sorted(lifetime.Size);
        _opacityCurve = lifetime.Opacity is { Count: > 0 } ? Sorted(lifetime.Opacity) : DefaultOpacityCurve;
        _velocityCurve = Sorted(lifetime.Velocity);
        _rotationCurve = Sorted(lifetime.RotationDegrees);
        _colorCurve = Sorted(lifetime.Color);
        EmissionRate = definition.EmissionRate; _velocityX = initial.VelocityX; _velocityY = initial.VelocityY;
        Noise = motion.Noise; _scale = initial.Size; _life = initial.Lifetime;
    }

    public string HandleId { get; }
    public double Z { get => _z; set => SetField(ref _z, value); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<ParticleEmitter>? Drained;
    public int ActiveParticleCount => _active.Count;
    public float EmissionRate { get => _rate; set => _rate = Math.Max(0, value); }
    public float InitialVelocityX { get => _velocityX.Center; set => _velocityX = _velocityX.WithCenter(value); }
    public float InitialVelocityY { get => _velocityY.Center; set => _velocityY = _velocityY.WithCenter(value); }
    public float Noise { get => _noise; set => _noise = Math.Max(0, value); }
    public float ParticleScale { get => _scale.Center; set => _scale = _scale.WithCenter(Math.Max(.001f, value)); }
    public float ParticleLifetime { get => _life.Center; set => _life = _life.WithCenter(Math.Max(.01f, value)); }

    /// <summary>Stops spawning but keeps existing particles visible until their normal lifetime ends.</summary>
    public void StopEmission() { _stopping = true; EmissionRate = 0; }

    public bool Update(SceneFrameContext frame)
    {
        var delta = Math.Max(0, (float)frame.Delta.TotalSeconds);
        if (_pendingBurstCount > 0)
        {
            Spawn(frame.Size, _pendingBurstCount);
            _pendingBurstCount = 0;
            _stopping = true;
        }
        if (!_stopping)
        {
            _emissionCarry += _rate * delta;
            var continuousCount = Math.Min((int)Math.Floor(_emissionCarry), _maximum - _active.Count);
            if (continuousCount > 0)
            {
                _emissionCarry -= continuousCount;
                Spawn(frame.Size, continuousCount);
            }
        }
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            var particle = _active[index];
            particle.Age += delta;
            var accelerationX = _gravityX + ((float)_random.NextDouble() - .5f) * _noise;
            var accelerationY = _gravityY;
            AddAttractorAcceleration(particle, ref accelerationX, ref accelerationY);
            particle.Vx += accelerationX * delta;
            particle.Vy += accelerationY * delta;
            if (_drag > 0)
            {
                var damping = MathF.Exp(-_drag * delta);
                particle.Vx *= damping;
                particle.Vy *= damping;
            }

            var progress = Math.Clamp(particle.Age / particle.Life, 0, 1);
            var velocityMultiplier = Sample(_velocityCurve, progress, 1);
            particle.X += particle.Vx * delta * velocityMultiplier;
            particle.Y += particle.Vy * delta * velocityMultiplier;
            ApplyOrbit(particle, delta);
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

        EnsureAtlasCapacity(_active.Count);
        if (_lastRenderedParticleCount > _active.Count)
            Array.Clear(_colors, _active.Count, _lastRenderedParticleCount - _active.Count);
        for (var index = 0; index < _active.Count; index++)
        {
            var particle = _active[index];
            var progress = Math.Clamp(particle.Age / particle.Life, 0, 1);
            var size = 28f * particle.Scale * Math.Max(0, Sample(_sizeCurve, progress, 1));
            var source = GetSource(image, particle);
            var scale = size / Math.Max(1, source.Width);
            var rotation = (particle.Rotation + particle.AngularVelocity * particle.Age + Sample(_rotationCurve, progress, 0)) * MathF.PI / 180f;
            var cosine = MathF.Cos(rotation) * scale;
            var sine = MathF.Sin(rotation) * scale;
            // DrawAtlas applies RSXform to source-local coordinates, not the atlas-space rect.
            var centerX = source.Width * .5f;
            var centerY = source.Height * .5f;
            _sprites[index] = source;
            _transforms[index] = new SKRotationScaleMatrix(
                cosine,
                sine,
                particle.X - cosine * centerX + sine * centerY,
                particle.Y - sine * centerX - cosine * centerY);
            var color = Multiply(particle.InitialColor, SampleColor(progress));
            var opacity = Math.Clamp(Sample(_opacityCurve, progress, 1), 0, 1);
            _colors[index] = color.WithAlpha((byte)Math.Clamp(Math.Round(color.Alpha * opacity), 0, 255));
        }
        using var paint = new SKPaint { IsAntialias = true };
        context.Canvas.DrawAtlas(image, _sprites, _transforms, _colors, SKBlendMode.Modulate, paint);
        _lastRenderedParticleCount = _active.Count;
    }

    private void Spawn(SKSize size, int count)
    {
        for (var index = 0; index < count && _active.Count < _maximum; index++) SpawnOne(size);
    }

    private void SpawnOne(SKSize size)
    {
        var particle = _pool.TryPop(out var reused) ? reused : new Particle();
        (particle.X, particle.Y) = SampleSpawnPosition(size);
        particle.Vx = Sample(_velocityX);
        particle.Vy = Sample(_velocityY);
        AddRadialVelocity(particle);
        particle.Age = 0;
        particle.Life = Math.Max(.01f, Sample(_life));
        particle.Scale = Math.Max(.001f, Sample(_scale));
        particle.Rotation = Sample(_rotation);
        particle.AngularVelocity = Sample(_angularVelocity);
        particle.InitialColor = Sample(_initialColor);
        particle.FlipbookRate = _flipbook is null ? 0 : Sample(_flipbook.PlaybackRate);
        particle.StartFrame = _flipbook is null ? 0 : Sample(_flipbook.StartFrameRange);
        _active.Add(particle);
    }

    private SKRect GetSource(SKImage image, Particle particle)
    {
        if (_flipbook is not { IsValid: true } flipbook)
            return new SKRect(0, 0, image.Width, image.Height);
        var frame = flipbook.GetFrameIndex(particle.Age, particle.Life, particle.FlipbookRate, particle.StartFrame);
        var cellWidth = image.Width / (float)flipbook.Columns;
        var cellHeight = image.Height / (float)flipbook.Rows;
        var column = frame % flipbook.Columns;
        var row = frame / flipbook.Columns;
        return new SKRect(column * cellWidth, row * cellHeight, (column + 1) * cellWidth, (row + 1) * cellHeight);
    }

    private (float X, float Y) SampleSpawnPosition(SKSize size)
    {
        if (_shape is null) return (0, 0);
        return _shape.Type switch
        {
            ParticleShapeKind.Point => (_shape.X, _shape.Y),
            ParticleShapeKind.Box => (
                _shape.X + ((float)_random.NextDouble() - .5f) * _shape.Width,
                _shape.Y + ((float)_random.NextDouble() - .5f) * _shape.Height),
            ParticleShapeKind.Circle => SampleCircle(_shape),
            ParticleShapeKind.Line => SampleLine(_shape),
            _ => (_shape.X, _shape.Y)
        };
    }

    private (float X, float Y) SampleCircle(ParticleShapeDefinition shape)
    {
        var angle = (float)(_random.NextDouble() * Math.PI * 2);
        var radius = MathF.Sqrt((float)_random.NextDouble()) * shape.Radius;
        return (shape.X + MathF.Cos(angle) * radius, shape.Y + MathF.Sin(angle) * radius);
    }

    private (float X, float Y) SampleLine(ParticleShapeDefinition shape)
    {
        var amount = (float)_random.NextDouble();
        return (shape.X + (shape.EndX - shape.X) * amount, shape.Y + (shape.EndY - shape.Y) * amount);
    }

    private void AddRadialVelocity(Particle particle)
    {
        if (_radialVelocity == 0) return;
        var (centerX, centerY) = GetEmitterCenter();
        var directionX = particle.X - centerX;
        var directionY = particle.Y - centerY;
        var length = MathF.Sqrt(directionX * directionX + directionY * directionY);
        if (length <= .0001f)
        {
            var angle = (float)(_random.NextDouble() * Math.PI * 2);
            directionX = MathF.Cos(angle); directionY = MathF.Sin(angle); length = 1;
        }
        particle.Vx += directionX / length * _radialVelocity;
        particle.Vy += directionY / length * _radialVelocity;
    }

    private void AddAttractorAcceleration(Particle particle, ref float accelerationX, ref float accelerationY)
    {
        if (_attractor is not { Strength: not 0 } attractor) return;
        var directionX = attractor.X - particle.X;
        var directionY = attractor.Y - particle.Y;
        var length = MathF.Sqrt(directionX * directionX + directionY * directionY);
        if (length <= .0001f) return;
        accelerationX += directionX / length * attractor.Strength;
        accelerationY += directionY / length * attractor.Strength;
    }

    private void ApplyOrbit(Particle particle, float delta)
    {
        if (_orbitRadiansPerSecond == 0 || delta == 0) return;
        var (centerX, centerY) = GetEmitterCenter();
        var x = particle.X - centerX;
        var y = particle.Y - centerY;
        var angle = _orbitRadiansPerSecond * delta;
        var cosine = MathF.Cos(angle);
        var sine = MathF.Sin(angle);
        particle.X = centerX + x * cosine - y * sine;
        particle.Y = centerY + x * sine + y * cosine;
    }

    private (float X, float Y) GetEmitterCenter() => _shape switch
    {
        { Type: ParticleShapeKind.Line } line => ((line.X + line.EndX) * .5f, (line.Y + line.EndY) * .5f),
        not null => (_shape.X, _shape.Y),
        _ => (0, 0)
    };

    private void RenderFallback(SceneRenderContext context)
    {
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var particle in _active)
        {
            var progress = Math.Clamp(particle.Age / particle.Life, 0, 1);
            var color = Multiply(particle.InitialColor, SampleColor(progress));
            var opacity = Math.Clamp(Sample(_opacityCurve, progress, 1), 0, 1);
            paint.Color = color.WithAlpha((byte)Math.Clamp(Math.Round(color.Alpha * opacity), 0, 255));
            var size = 28f * particle.Scale * Math.Max(0, Sample(_sizeCurve, progress, 1));
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

    private float Sample(ParticleFloatRange range) => range.Min + (range.Max - range.Min) * (float)_random.NextDouble();
    private int Sample(ParticleIntRange range) => range.Min == range.Max ? range.Min : _random.Next(range.Min, range.Max + 1);
    private SKColor Sample(ParticleColorRange range)
    {
        if (!TryParseColor(range.Min, out var minimum) || !TryParseColor(range.Max, out var maximum)) return SKColors.White;
        return Lerp(minimum, maximum, (float)_random.NextDouble());
    }

    private SKColor SampleColor(float progress)
    {
        if (_colorCurve.Length == 0) return SKColors.White;
        if (_colorCurve.Length == 1 || progress <= _colorCurve[0].Time)
            return ParseColorOrWhite(_colorCurve[0].Color);
        for (var index = 1; index < _colorCurve.Length; index++)
        {
            var next = _colorCurve[index];
            if (progress > next.Time) continue;
            var previous = _colorCurve[index - 1];
            var amount = InverseLerp(previous.Time, next.Time, progress);
            return Lerp(ParseColorOrWhite(previous.Color), ParseColorOrWhite(next.Color), amount);
        }
        return ParseColorOrWhite(_colorCurve[^1].Color);
    }

    private static float Sample(IReadOnlyList<ParticleCurveKey> curve, float progress, float fallback)
    {
        if (curve.Count == 0) return fallback;
        if (curve.Count == 1 || progress <= curve[0].Time) return curve[0].Value;
        for (var index = 1; index < curve.Count; index++)
        {
            var next = curve[index];
            if (progress > next.Time) continue;
            var previous = curve[index - 1];
            return previous.Value + (next.Value - previous.Value) * InverseLerp(previous.Time, next.Time, progress);
        }
        return curve[^1].Value;
    }

    private static float InverseLerp(float from, float to, float value) => Math.Abs(to - from) < float.Epsilon ? 1 : Math.Clamp((value - from) / (to - from), 0, 1);
    private static ParticleCurveKey[] Sorted(IReadOnlyList<ParticleCurveKey>? curve) => curve?.OrderBy(key => key.Time).ToArray() ?? [];
    private static ParticleColorCurveKey[] Sorted(IReadOnlyList<ParticleColorCurveKey>? curve) => curve?.OrderBy(key => key.Time).ToArray() ?? [];
    private static SKColor ParseColorOrWhite(string value) => TryParseColor(value, out var color) ? color : SKColors.White;
    private static SKColor Lerp(SKColor from, SKColor to, float amount) => new(
        (byte)Math.Clamp(Math.Round(from.Red + (to.Red - from.Red) * amount), 0, 255),
        (byte)Math.Clamp(Math.Round(from.Green + (to.Green - from.Green) * amount), 0, 255),
        (byte)Math.Clamp(Math.Round(from.Blue + (to.Blue - from.Blue) * amount), 0, 255),
        (byte)Math.Clamp(Math.Round(from.Alpha + (to.Alpha - from.Alpha) * amount), 0, 255));
    private static SKColor Multiply(SKColor left, SKColor right) => new(
        (byte)(left.Red * right.Red / 255),
        (byte)(left.Green * right.Green / 255),
        (byte)(left.Blue * right.Blue / 255),
        (byte)(left.Alpha * right.Alpha / 255));
    private static bool TryParseColor(string value, out SKColor color)
    {
        color = SKColors.White;
        if (string.IsNullOrWhiteSpace(value) || value[0] != '#') return false;
        var hex = value[1..];
        if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); return true;
        }
        if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba))
        {
            color = new SKColor((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba); return true;
        }
        return false;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
