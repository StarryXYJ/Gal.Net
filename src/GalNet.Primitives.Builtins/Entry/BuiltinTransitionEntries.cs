using GalNet.Core.Entry;

namespace GalNet.Primitives.Builtins;

/// <summary>
/// Recommended authoring-only transition entries. They expand into the
/// animation, layer and effect primitives selected by the target profile.
/// </summary>
internal static class BuiltinTransitionEntries
{
    public static IReadOnlyList<CompositeEntryBase> Definitions { get; } =
    [
        Define(CrossFadeTransitionEntry.TypeId, () => new CrossFadeTransitionEntry(), CrossFadeTransitionEntry.ParameterTypes, CrossFadeTransitionEntry.DefaultValues, CrossFadeTransitionEntry.ParameterOptions),
        Define(SlideTransitionEntry.TypeId, () => new SlideTransitionEntry(), SlideTransitionEntry.ParameterTypes, SlideTransitionEntry.DefaultValues, SlideTransitionEntry.ParameterOptions),
        Define(BlindsTransitionEntry.TypeId, () => new BlindsTransitionEntry(), BlindsTransitionEntry.ParameterTypes, BlindsTransitionEntry.DefaultValues, BlindsTransitionEntry.ParameterOptions),
        Define(BlackFadeTransitionEntry.TypeId, () => new BlackFadeTransitionEntry(), BlackFadeTransitionEntry.ParameterTypes, BlackFadeTransitionEntry.DefaultValues, BlackFadeTransitionEntry.ParameterOptions),
        Define(WhiteFadeTransitionEntry.TypeId, () => new WhiteFadeTransitionEntry(), WhiteFadeTransitionEntry.ParameterTypes, WhiteFadeTransitionEntry.DefaultValues, WhiteFadeTransitionEntry.ParameterOptions),
        Define(ColorFadeTransitionEntry.TypeId, () => new ColorFadeTransitionEntry(), ColorFadeTransitionEntry.ParameterTypes, ColorFadeTransitionEntry.DefaultValues, ColorFadeTransitionEntry.ParameterOptions)
    ];

    private static CompositeEntryBase Define(
        string type,
        Func<CompositeEntry> factory,
        IReadOnlyDictionary<string, EntryParameterType> parameters,
        IReadOnlyDictionary<string, string> defaults,
        IReadOnlyDictionary<string, IReadOnlyList<string>> options) =>
        new DefaultCompositeEntryBase(
            type,
            EntrySchema.DynamicParameters(parameters, defaults, options),
            factory,
            "Transition");
}
