using System.Collections.Frozen;
using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Contracts.Builtins.Scene;

namespace GalNet.Contracts.Builtins;

/// <summary>The built-in Timeline event allow-list shared by authoring and contract validation.</summary>
public static class TimelineEventCapabilities
{
    public static IReadOnlyList<string> Types { get; } = Array.AsReadOnly(new[]
    {
        BuiltinEntryIds.ShowLayer, BuiltinEntryIds.ShowColorLayer, BuiltinEntryIds.HideLayer,
        BuiltinEntryIds.MoveLayer, BuiltinEntryIds.ReplaceLayer, BuiltinEntryIds.ApplyEffect,
        BuiltinEntryIds.StopEffect, BuiltinEntryIds.PlayParticleEmitter,
        BuiltinEntryIds.StopParticleEmitter, BuiltinEntryIds.BurstParticles
    });
    private static readonly FrozenSet<string> Supported = Types.ToFrozenSet(StringComparer.Ordinal);
    private static readonly Lazy<IEntryCatalog> Catalog = new(() => new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()));
    public static bool Supports(string type) => Supported.Contains(type);
    public static string? CreationSlot(string type) => type switch
    {
        BuiltinEntryIds.ShowLayer or BuiltinEntryIds.ShowColorLayer => "handleId",
        BuiltinEntryIds.PlayParticleEmitter or BuiltinEntryIds.ApplyEffect => "instanceId",
        _ => null
    };
    public static JsonElement NormalizeEvent(TimelineEventDefinition item)
    {
        if (item is null || item.Parameters is null || !Supports(item.Type)) throw new InvalidDataException("Unsupported or incomplete Timeline event.");
        return PrimitiveArgumentHelper.Normalize(Catalog.Value.Get(item.Type).DynamicParameters, JsonSerializer.SerializeToElement(item.Parameters));
    }
    public static void ValidateStructure(TimelineDefinition timeline) => BuiltinArgumentValidation.ValidateTimelineStructure(timeline);
}
