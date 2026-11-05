using System.Text.Json;
using GalNet.Contracts.Builtins;

namespace GalNet.Editor.Shared.Visuals;

/// <summary>Editable authoring snapshots; applying one produces a normal particle.play entry.</summary>
public static class DefaultParticlePresets
{
    public const string Path = "Buildin/Presets/entries.json";
    public const string AssetId = "c1000000000000000000000000000000";
    public static IReadOnlyList<string> Ids { get; } = Array.AsReadOnly(Enumerable.Range(1, 5).Select(index => $"c100000000000000000000000000000{index}").ToArray());
    public static byte[] Create()
    {
        var types = new[] { ("Rain", "雨"), ("Snow", "雪"), ("Petals", "花瓣"), ("Dust", "尘埃"), ("Fireflies", "萤火虫") };
        var presets = types.Select((item, index) =>
        {
            var rain = item.Item1 == "Rain";
            var floating = item.Item1 is "Dust" or "Fireflies";
            var speed = rain ? 1000f : item.Item1 == "Snow" ? 100f : 70f;
            var minSize = item.Item1 switch { "Rain" => .0572f, "Snow" => .15f, "Petals" => .45f, "Dust" => .07f, _ => .12f };
            var maxSize = item.Item1 switch { "Rain" => .143f, "Snow" => .45f, "Petals" => 1f, "Dust" => .18f, _ => .35f };
            var minLife = floating ? 3f : 1080f / speed + 1;
            var maxLife = floating ? 6f : 1080f / speed + 2;
            var wind = rain ? 250f : 0f;
            var drift = floating ? 0 : wind * 1112 / (speed * .8f);
            return new { id = Ids[index], name = item.Item2, type = BuiltinEntryIds.PlayParticleEmitter,
                parameters = new { z = 100, parameters = new {
                    particleTexture = BuiltinVisualResources.WeatherTextures[item.Item1], rate = Math.Min(40, 300 / (maxLife + .1f)), maxParticles = 300, seed = 20261102,
                    initial = new { lifetime = new { min = minLife, max = maxLife }, velocityX = new { min = wind - 15, max = wind + 15 },
                        velocityY = new { min = floating ? -15 : speed * .8f, max = floating ? 15 : speed * 1.2f }, alignToVelocity = rain,
                        size = new { min = minSize, max = maxSize }, rotationDegrees = new { min = rain ? 0 : -180, max = rain ? 0 : 180 },
                        angularVelocityDegrees = new { min = item.Item1 == "Petals" ? -90 : 0, max = item.Item1 == "Petals" ? 90 : 0 },
                        color = new { min = rain ? "#7FA9C730" : item.Item1 == "Petals" ? "#ED4786CC" : item.Item1 == "Fireflies" ? "#FFF0A088" : "#FFFFFFFF",
                            max = rain ? "#9FC6DF78" : item.Item1 == "Petals" ? "#FFB1D1F2" : item.Item1 == "Fireflies" ? "#FFF7C0FF" : "#FFFFFFFF" } },
                    shape = new { type = "box", x = 960 - drift / 2, y = floating ? 540 : -16, width = 1920 + Math.Abs(drift), height = floating ? 1080 : 1 },
                    motion = new { noise = rain ? 0 : floating ? 15 : 20, drag = floating ? .15 : 0 },
                    lifetime = new { opacity = new[] { new { time = 0f, value = 0f }, new { time = Math.Min(.08f, .12f / minLife), value = 1f }, new { time = .8f, value = 1f }, new { time = 1f, value = 0f } } }
                } } };
        }).ToArray();
        return JsonSerializer.SerializeToUtf8Bytes(new { version = 1, presets }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    }
}
