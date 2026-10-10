using GalNet.Core.Gallery;
using GalNet.Presentation.Abstractions.Audio;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

/// <summary>Independent schema and implementation registration for one game scope.</summary>
public static class BuiltinRegistration
{
    public static GameRegistration AddBuiltinSchemas(this GameRegistration registration) =>
        registration.RegisterSchema(BuiltinSchemas.CreateModule());

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
        IVideoPresenter? videoPresenter = null) => registration.AddBuiltinSchemas().AddBuiltinImplementations(
            dialoguePresenter, layerPresenter, animationPresenter, effectPresenter, gallery,
            particlePresenter, nvlPresenter, audioHost, videoPresenter);

    /// <summary>Registers logical implementations. Optional presenters select state-only/headless operation.</summary>
    public static GameRegistration AddBuiltinImplementations(
        this GameRegistration registration,
        IDialoguePresenter? dialoguePresenter = null,
        ILayerPresenter? layerPresenter = null,
        IAnimationPresenter? animationPresenter = null,
        IEffectPresenter? effectPresenter = null,
        GalleryCatalog? gallery = null,
        IParticlePresenter? particlePresenter = null,
        INvlPresenter? nvlPresenter = null,
        IAudioPlaybackHost? audioHost = null,
        IVideoPresenter? videoPresenter = null) => registration
            .AddBuiltinDialogue(dialoguePresenter)
            .AddBuiltinNvl(nvlPresenter)
            .AddBuiltinLayers(layerPresenter)
            .AddBuiltinAnimations(animationPresenter, layerPresenter, effectPresenter, particlePresenter)
            .AddBuiltinAudio(audioHost)
            .AddBuiltinVideo(videoPresenter)
            .AddBuiltinEffects(effectPresenter)
            .AddBuiltinParticles(particlePresenter)
            .AddBuiltinFlow()
            .AddBuiltinVariables()
            .AddBuiltinGallery(gallery);

    public static GameRegistration AddBuiltinVariables(this GameRegistration registration) => registration.RegisterImplementation(new BuiltinVariableModule());
    public static GameRegistration AddBuiltinFlow(this GameRegistration registration) => registration.RegisterImplementation(new BuiltinFlowModule());
    public static GameRegistration AddBuiltinAudio(this GameRegistration registration, IAudioPlaybackHost? host = null) => registration.RegisterImplementation(new BuiltinAudioModule(host));
    public static GameRegistration AddBuiltinGallery(this GameRegistration registration, GalleryCatalog? gallery = null) => registration.RegisterImplementation(new BuiltinGalleryModule(gallery));
    public static GameRegistration AddBuiltinDialogue(this GameRegistration registration, IDialoguePresenter? presenter = null) => registration.RegisterImplementation(new BuiltinDialogueModule(presenter));
    public static GameRegistration AddBuiltinNvl(this GameRegistration registration, INvlPresenter? presenter = null) => registration.RegisterImplementation(new BuiltinNvlModule(presenter));
    public static GameRegistration AddBuiltinLayers(this GameRegistration registration, ILayerPresenter? presenter = null) => registration.RegisterImplementation(new BuiltinLayerModule(presenter));
    public static GameRegistration AddBuiltinEffects(this GameRegistration registration, IEffectPresenter? presenter = null) => registration.RegisterImplementation(new BuiltinEffectModule(presenter));
    public static GameRegistration AddBuiltinParticles(this GameRegistration registration, IParticlePresenter? presenter = null) => registration.RegisterImplementation(new BuiltinParticleModule(presenter));
    public static GameRegistration AddBuiltinVideo(this GameRegistration registration, IVideoPresenter? presenter = null) => registration.RegisterImplementation(new BuiltinVideoModule(presenter));
    public static GameRegistration AddBuiltinAnimations(this GameRegistration registration,
        IAnimationPresenter? animations = null, ILayerPresenter? layers = null,
        IEffectPresenter? effects = null, IParticlePresenter? particles = null) =>
        registration.RegisterImplementation(new BuiltinAnimationModule(animations, layers, effects, particles));
}
