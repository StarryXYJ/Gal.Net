using GalNet.Core.Entry;

namespace GalNet.Contracts.Builtins;

internal static class BuiltinTransitionEntries
{
    public static IReadOnlyList<CompositeEntryBase> Definitions { get; } =
    [
        DefaultCompositeEntryBase.ForDto<RevealTransitionOptions>(BuiltinEntryIds.RevealTransition, VisualPresetCompiler.Reveal, category: "Transition"),
        DefaultCompositeEntryBase.ForDto<BlurTransitionOptions>(BuiltinEntryIds.BlurTransition, VisualPresetCompiler.Blur, category: "Transition"),
        DefaultCompositeEntryBase.ForDto<CrossFadeOptions>(BuiltinEntryIds.CrossFadeTransition, CrossFadeTransition.Compile, category: "Transition"),
        DefaultCompositeEntryBase.ForDto<SlideOptions>(BuiltinEntryIds.SlideTransition, SlideTransition.Compile, category: "Transition"),
        DefaultCompositeEntryBase.ForDto<BlindsOptions>(BuiltinEntryIds.BlindsTransition, BlindsTransition.Compile, category: "Transition"),
        DefaultCompositeEntryBase.ForDto<ColorFieldOptions>(BuiltinEntryIds.BlackFadeTransition, (context, options) => ColorFieldTransition.Compile(context, options, "#000000", BuiltinEntryIds.BlackFadeTransition), category: "Transition"),
        DefaultCompositeEntryBase.ForDto<ColorFieldOptions>(BuiltinEntryIds.WhiteFadeTransition, (context, options) => ColorFieldTransition.Compile(context, options, "#FFFFFF", BuiltinEntryIds.WhiteFadeTransition), category: "Transition"),
        DefaultCompositeEntryBase.ForDto<ColorFadeOptions>(BuiltinEntryIds.ColorFadeTransition, (context, options) => ColorFieldTransition.Compile(context, options, options.Color, BuiltinEntryIds.ColorFadeTransition), category: "Transition")
    ];
}
