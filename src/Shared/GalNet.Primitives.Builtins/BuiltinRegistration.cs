using GalNet.Contracts.Builtins;
using GalNet.Core.Audio;
using GalNet.Core.Entry;
using GalNet.Core.Gallery;
using GalNet.Core.I18n;
using GalNet.Core.Variants;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

/// <summary>Independent schema and implementation registration for one game scope.</summary>
public static class BuiltinRegistration
{
    public static GameRegistration AddBuiltinSchemas(this GameRegistration registration)
    {
        foreach (var module in BuiltinSchemas.CreateAllModules())
            registration.RegisterSchema(module);
        return registration;
    }

    public static GameRegistration AddBuiltins(
        this GameRegistration registration,
        IDialoguePresenter? dialoguePresenter = null,
        ILayerPresenter? layerPresenter = null,
        IAnimationPresenter? animationPresenter = null,
        IEffectPresenter? effectPresenter = null,
        GalleryCatalog? gallery = null,
        IParticlePresenter? particlePresenter = null,
        INvlPresenter? nvlPresenter = null,
        IAudioPlaybackHost? audioHost = null,
        IVideoPresenter? videoPresenter = null,
        CharacterCatalog? characters = null,
        BuiltinAudioModule? audioModule = null,
        ImageVariantsCatalog? imageVariants = null,
        BuiltinCustomActionModule? customActions = null,
        IReadOnlyList<AudioTrackDefinition>? audioTracks = null)
    {
        registration.AddBuiltinSchemas();
        return registration.AddBuiltinImplementations(registration.BuildCatalog(),
            dialoguePresenter, layerPresenter, animationPresenter, effectPresenter, gallery,
            particlePresenter, nvlPresenter, audioHost, videoPresenter, characters, audioModule, imageVariants, customActions, audioTracks);
    }

    /// <summary>Registers logical implementations. Optional presenters select state-only/headless operation.</summary>
    public static GameRegistration AddBuiltinImplementations(
        this GameRegistration registration,
        IEntryCatalog catalog,
        IDialoguePresenter? dialoguePresenter = null,
        ILayerPresenter? layerPresenter = null,
        IAnimationPresenter? animationPresenter = null,
        IEffectPresenter? effectPresenter = null,
        GalleryCatalog? gallery = null,
        IParticlePresenter? particlePresenter = null,
        INvlPresenter? nvlPresenter = null,
        IAudioPlaybackHost? audioHost = null,
        IVideoPresenter? videoPresenter = null,
        CharacterCatalog? characters = null,
        BuiltinAudioModule? audioModule = null,
        ImageVariantsCatalog? imageVariants = null,
        BuiltinCustomActionModule? customActions = null,
        IReadOnlyList<AudioTrackDefinition>? audioTracks = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        IPrimitiveModule[] modules =
        [
            new BuiltinDialogueModule(dialoguePresenter, characters), new BuiltinNvlModule(nvlPresenter),
            new BuiltinLayerModule(layerPresenter, imageVariants), new BuiltinAnimationModule(animationPresenter, layerPresenter, effectPresenter, particlePresenter),
            audioModule ?? new BuiltinAudioModule(audioHost, tracks: audioTracks), new BuiltinVideoModule(videoPresenter),
            new BuiltinEffectModule(effectPresenter), new BuiltinParticleModule(particlePresenter),
            new BuiltinFlowModule(), new BuiltinVariableModule(), new BuiltinGalleryModule(gallery),
            customActions ?? new BuiltinCustomActionModule()
        ];
        foreach (var module in modules) registration.RegisterImplementation(new SelectedBuiltinModule(module, catalog));
        return registration;
    }

    // Selection limits exposed instructions while retaining the whole component's shared state and ownership.
    private sealed class SelectedBuiltinModule(IPrimitiveModule inner, IEntryCatalog catalog) : IPrimitiveModule
    {
        public void Register(PrimitiveRegistry registry) => registry.RegisterDeclared(inner, catalog);
        private bool _disposed;
        public void Dispose() { if (_disposed) return; _disposed = true; inner.Dispose(); }
        public async ValueTask DisposeAsync() { if (_disposed) return; _disposed = true; await inner.DisposeAsync().ConfigureAwait(false); }
    }

    public static GameRegistration AddBuiltinVariables(this GameRegistration registration) => registration.RegisterImplementation(new BuiltinVariableModule());
    public static GameRegistration AddBuiltinFlow(this GameRegistration registration) => registration.RegisterImplementation(new BuiltinFlowModule());
    public static GameRegistration AddBuiltinCustomActions(this GameRegistration registration, BuiltinCustomActionModule? module = null) =>
        registration.RegisterImplementation(module ?? new BuiltinCustomActionModule());
    public static GameRegistration AddBuiltinAudio(this GameRegistration registration, IAudioPlaybackHost? host = null,
        IReadOnlyList<AudioTrackDefinition>? tracks = null) => registration.RegisterImplementation(new BuiltinAudioModule(host, tracks: tracks));
    public static GameRegistration AddBuiltinGallery(this GameRegistration registration, GalleryCatalog? gallery = null) => registration.RegisterImplementation(new BuiltinGalleryModule(gallery));
    public static GameRegistration AddBuiltinDialogue(this GameRegistration registration, IDialoguePresenter? presenter = null, CharacterCatalog? characters = null) => registration.RegisterImplementation(new BuiltinDialogueModule(presenter, characters));
    public static GameRegistration AddBuiltinNvl(this GameRegistration registration, INvlPresenter? presenter = null) => registration.RegisterImplementation(new BuiltinNvlModule(presenter));
    public static GameRegistration AddBuiltinLayers(this GameRegistration registration, ILayerPresenter? presenter = null,
        ImageVariantsCatalog? imageVariants = null) => registration.RegisterImplementation(new BuiltinLayerModule(presenter, imageVariants));
    public static GameRegistration AddBuiltinEffects(this GameRegistration registration, IEffectPresenter? presenter = null) => registration.RegisterImplementation(new BuiltinEffectModule(presenter));
    public static GameRegistration AddBuiltinParticles(this GameRegistration registration, IParticlePresenter? presenter = null) => registration.RegisterImplementation(new BuiltinParticleModule(presenter));
    public static GameRegistration AddBuiltinVideo(this GameRegistration registration, IVideoPresenter? presenter = null) => registration.RegisterImplementation(new BuiltinVideoModule(presenter));
    public static GameRegistration AddBuiltinAnimations(this GameRegistration registration,
        IAnimationPresenter? animations = null, ILayerPresenter? layers = null,
        IEffectPresenter? effects = null, IParticlePresenter? particles = null) =>
        registration.RegisterImplementation(new BuiltinAnimationModule(animations, layers, effects, particles));
}
