using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>CPU particle emitter with per-emitter pooling. Stopping ends emission but preserves live particles.</summary>
public sealed class ParticleEmitterControl : Control, IDisposable
{
    private sealed class Particle { public double X; public double Y; public double Vx; public double Vy; public double Age; public double Life; public double Scale; }
    private readonly List<Particle> _active = [];
    private readonly Stack<Particle> _pool = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _random;
    private readonly IImage? _texture;
    private double _rate;
    private double _speedX;
    private double _speedY;
    private double _noise;
    private double _scale;
    private double _life;
    private readonly int _maximum;
    private double _lastSeconds;
    private double _emissionCarry;
    private bool _stopping;

    public event Action<ParticleEmitterControl>? Drained;

    /// <summary>Parameters sampled when the next particle is emitted; each accepts common Effect animation input.</summary>
    public double EmissionRate { get => _rate; set => _rate = Math.Max(0, value); }
    public double InitialVelocityX { get => _speedX; set => _speedX = value; }
    public double InitialVelocityY { get => _speedY; set => _speedY = value; }
    public double Noise { get => _noise; set => _noise = Math.Max(0, value); }
    public double ParticleScale { get => _scale; set => _scale = Math.Max(.001, value); }
    public double ParticleLifetime { get => _life; set => _life = Math.Max(.01, value); }

    public ParticleEmitterControl(IImage? texture, string parameters)
    {
        var (rate, maximum, speedX, speedY, noise, scale, life, seed) = ReadSettings(parameters);
        EmissionRate = rate;
        _maximum = Math.Max(1, maximum);
        InitialVelocityX = speedX;
        InitialVelocityY = speedY;
        Noise = noise;
        ParticleScale = scale;
        ParticleLifetime = life;
        _texture = texture;
        _random = new Random(seed);
        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>Stops creating particles; existing particles remain until their configured lifetime elapses.</summary>
    public void StopEmission() { _stopping = true; EmissionRate = 0; }

    private void Tick()
    {
        var seconds = _clock.Elapsed.TotalSeconds;
        var delta = Math.Min(0.05, Math.Max(0, seconds - _lastSeconds));
        _lastSeconds = seconds;
        if (!_stopping)
        {
            _emissionCarry += _rate * delta;
            while (_emissionCarry >= 1 && _active.Count < _maximum) { _emissionCarry--; Spawn(); }
        }
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            var particle = _active[index];
            particle.Age += delta;
            particle.Vx += ((_random.NextDouble() - .5) * _noise) * delta;
            particle.X += particle.Vx * delta;
            particle.Y += particle.Vy * delta;
            if (particle.Age < particle.Life) continue;
            _active.RemoveAt(index); _pool.Push(particle);
        }
        InvalidateVisual();
        if (_stopping && _active.Count == 0) { _timer.Stop(); Drained?.Invoke(this); }
    }

    private void Spawn()
    {
        var particle = _pool.TryPop(out var reused) ? reused : new Particle();
        particle.X = _random.NextDouble() * Math.Max(1, Bounds.Width);
        particle.Y = -12;
        particle.Vx = _speedX + ((_random.NextDouble() - .5) * _noise);
        particle.Vy = _speedY + ((_random.NextDouble() - .5) * _noise);
        particle.Age = 0; particle.Life = _life * (.75 + (_random.NextDouble() * .5)); particle.Scale = _scale * (.75 + (_random.NextDouble() * .5));
        _active.Add(particle);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        foreach (var particle in _active)
        {
            var opacity = Math.Clamp(1 - (particle.Age / particle.Life), 0, 1);
            using (context.PushOpacity(opacity))
            {
                var size = 28 * particle.Scale;
                var destination = new Rect(particle.X - (size / 2), particle.Y - (size / 2), size, size);
                if (_texture is not null) context.DrawImage(_texture, new Rect(_texture.Size), destination);
                else context.DrawEllipse(Brushes.White, null, destination.Center, size / 2, size / 2);
            }
        }
    }

    public void Dispose() { _timer.Stop(); _active.Clear(); _pool.Clear(); }

    private static (double Rate, int Maximum, double SpeedX, double SpeedY, double Noise, double Scale, double Life, int Seed) ReadSettings(string parameters)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(parameters) ? "{}" : parameters);
            var root = document.RootElement;
            static double Number(JsonElement root, string name, double fallback) => root.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) ? result : fallback;
            static int Integer(JsonElement root, string name, int fallback) => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;
            return (Number(root, "emissionRate", 36), Integer(root, "maxParticles", 160), Number(root, "initialVelocityX", -14), Number(root, "initialVelocityY", 96), Number(root, "noise", 18), Number(root, "particleScale", 1), Number(root, "particleLifetime", 3), Integer(root, "seed", 20260911));
        }
        catch (JsonException) { return (36, 160, -14, 96, 18, 1, 3, 20260911); }
    }
}
