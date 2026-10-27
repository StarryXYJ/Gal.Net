using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using GalNet.Contracts.Builtins;
using GalNet.Core.Parameters;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Editor.MarkupExtensions;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Abstraction.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>Editor-owned presentation extensions; value conversion and submission stay in the shared factory.</summary>
public static class EditorFieldEditors
{
    public static EditorRegistry CreateDefault(SceneTargetCatalog? scenes = null, Action<FieldEditorContext>? openTimeline = null, GalNet.Core.Assets.IAssetManager? assets = null, CharacterAuthoringService? characters = null, GameTextAuthoringService? texts = null, IProjectContentSession? content = null, ProjectSceneTargets? shaderPrograms = null)
    {
        var registry = EditorRegistry.CreateDefault(emptyInputClearsOptionalFields: true, copyValue: texts is null ? null : texts.StageCopy);
        if (characters is not null) registry.RegisterSemantic(FieldValueSemantic.CharacterReference, context => CharacterFieldEditor.Create(context, characters));
        if (texts is not null) registry.RegisterSemantic(FieldValueSemantic.LocalizedText, context => GameTextFieldEditor.Create(context, texts));
        registry.Register("entry.imageasset", context => AssetFieldEditor.Create(context, assets, Models.AssetPickerFilter.Image));
        registry.Register("entry.variantsasset", context => AssetFieldEditor.Create(context, assets, Models.AssetPickerFilter.ImageVariants));
        registry.Register("entry.effectprogramasset", context => AssetFieldEditor.Create(context, assets, Models.AssetPickerFilter.EffectProgram));
        registry.Register("entry.audioasset", context => AssetFieldEditor.Create(context, assets, Models.AssetPickerFilter.Audio));
        registry.Register("entry.videoasset", context => AssetFieldEditor.Create(context, assets, Models.AssetPickerFilter.Video));
        registry.Register("shader.color", context => ColorFieldEditor.Create(context, rgba: true));
        registry.Register("shader.vector2", context => ShaderVectorFieldEditor.Create(context, 2));
        registry.Register("shader.vector3", context => ShaderVectorFieldEditor.Create(context, 3));
        registry.Register("shader.vector4", context => ShaderVectorFieldEditor.Create(context, 4));
        foreach (var id in new[] { "entry.autocomplete", "entry.multilinetext" })
            registry.Register(id, context => DefaultFieldEditors.CreateText(context));
        registry.Register("entry.expression", context => AuthoringExpressionFieldEditors.Expression(context, () => [], () => []));
        registry.Register("entry.variablename", context => AuthoringExpressionFieldEditors.Variable(context, () => []));
        registry.RegisterDefault<LayerTransform>(TransformFieldEditor.Create);
        registry.RegisterMember<MotionPresetOptions>(value => value.Transform, TransformFieldEditor.Create);
        registry.RegisterMember<RevealTransitionOptions>(value => value.Center, context => ShaderVectorFieldEditor.Create(context, 2));
        registry.RegisterMember<SpeedLinesPresetOptions>(value => value.Parameters, context => PresetShaderParametersFieldEditor.Create(context, shaderPrograms));
        registry.RegisterMember<ImpactPresetOptions>(value => value.Parameters, context => PresetShaderParametersFieldEditor.Create(context, shaderPrograms));
        registry.RegisterMember<LayerShowOptions>(value => value.Flipbook, FlipbookFieldEditor.Create);
        FieldEditorLease Track(FieldEditorContext context) => AudioTrackFieldEditor.Create(context, content);
        registry.RegisterMember<AudioTrackOptions>(value => value.TrackId, Track);
        registry.RegisterMember<AudioStopOptions>(value => value.TrackId, Track);
        registry.RegisterMember<AudioPlayOptions>(value => value.TrackId, Track);
        FieldEditorLease AudioTarget(FieldEditorContext context) => AudioTrackFieldEditor.Create(context, content, target: true);
        registry.RegisterMember<AudioSEffectOptions>(value => value.TargetId, AudioTarget);
        registry.RegisterMember<AudioSEffectStopOptions>(value => value.TargetId, AudioTarget);
        registry.RegisterMember<AudioSEffectUpdateOptions>(value => value.TargetId, AudioTarget);
        registry.RegisterMember<AudioSEffectOptions>(value => value.Id, context => AudioEffectFieldEditor.Type(context, scenes));
        registry.RegisterMember<AudioSEffectOptions>(value => value.Parameters, context => AudioEffectParametersFieldEditor.Create(context, scenes));
        registry.RegisterMember<AudioSEffectUpdateOptions>(value => value.Parameters, context => AudioEffectParametersFieldEditor.Create(context, scenes));
        registry.RegisterMember<AudioSEffectStopOptions>(value => value.InstanceId, context => AudioEffectFieldEditor.Instance(context, scenes));
        registry.RegisterMember<AudioSEffectUpdateOptions>(value => value.InstanceId, context => AudioEffectFieldEditor.Instance(context, scenes));
        registry.RegisterMember<AudioLevelOptions>(value => value.TargetId, AudioTarget);
        registry.RegisterMember<TextVoiceOptions>(value => value.TrackId, Track);
        registry.RegisterMember<NvlVoiceOptions>(value => value.TrackId, Track);
        registry.RegisterMember<LayerShowOptions>(value => value.Transform, TransformFieldEditor.Create);
        registry.RegisterMember<VariantsShowOptions>(value => value.Transform, TransformFieldEditor.Create);
        registry.RegisterMember<LayerColorOptions>(value => value.Transform, TransformFieldEditor.Create);
        registry.RegisterMember<LayerMoveOptions>(value => value.Transform, TransformFieldEditor.Create);
        registry.RegisterMember<VideoPlayOptions>(value => value.Transform, TransformFieldEditor.Create);
        registry.RegisterMember<CrossFadeOptions>(value => value.Transform, TransformFieldEditor.Create);
        registry.RegisterMember<SlideOptions>(value => value.ToTransform, TransformFieldEditor.Create);
        registry.RegisterMember<BlindsOptions>(value => value.Transform, TransformFieldEditor.Create);
        registry.RegisterMember<ColorFieldOptions>(value => value.ToTransform, TransformFieldEditor.Create);
        registry.RegisterMember<ColorFadeOptions>(value => value.ToTransform, TransformFieldEditor.Create);
        registry.RegisterMember<LayerColorOptions>(value => value.Color, ColorFieldEditor.Create);
        registry.RegisterMember<ColorFadeOptions>(value => value.Color, ColorFieldEditor.Create);
        registry.RegisterMember<TimelineOptions>(value => value.Timeline, context => TimelineFieldEditor.Create(context, openTimeline));
        registry.Replace(ValueEditorIds.Choice, context =>
        {
            var lease = DefaultFieldEditors.CreateChoice(context);
            ((ComboBox)lease.Control).ItemTemplate = new FuncDataTemplate<FieldEditorOption>((option, _) =>
            {
                var localizedOption = option?.Label switch
                {
                    EntrySelectOption entryOption => entryOption,
                    string text => new EntrySelectOption(text, $"Entry.Option.{text}"),
                    _ => null
                };
                var label = new TextBlock { DataContext = localizedOption ?? option?.Label };
                if (localizedOption is not null)
                {
                    var value = new LocalizeExtension { Path = "DisplayNameKey", FallbackPath = "Value" }.ProvideValue(null!);
                    if (value is BindingBase binding) label.Bind(TextBlock.TextProperty, binding);
                    else label.Text = localizedOption.Value;
                }
                else label.Text = option?.Label?.ToString();
                return label;
            });
            return lease;
        });
        if (scenes is not null)
        {
            registry.RegisterMember<VariantsShowOptions>(options => options.Selection, context => VariantFieldEditor.Create(context, scenes, selection: true));
            registry.RegisterMember<VariantsSelectOptions>(options => options.Dimension, context => VariantFieldEditor.Create(context, scenes, selection: false, dimension: true));
            registry.RegisterMember<VariantsSelectOptions>(options => options.Value, context => VariantFieldEditor.Create(context, scenes, selection: false));
            FieldEditorLease Target(FieldEditorContext context) => SceneTargetFieldEditor.Create(context, scenes);
            registry.RegisterMember<ShakePresetOptions>(options => options.HandleId, Target);
            registry.RegisterMember<ImpactPresetOptions>(options => options.HandleId, Target);
            registry.RegisterMember<LayerHandleOptions>(options => options.HandleId, Target);
            registry.RegisterMember<LayerMoveOptions>(options => options.HandleId, Target);
            registry.RegisterMember<LayerReplaceOptions>(options => options.HandleId, Target);
            registry.RegisterMember<VariantsSelectOptions>(options => options.HandleId, Target);
            registry.RegisterMember<AnimationOptions>(options => options.HandleId, Target);
            registry.RegisterMember<AnimationStopOptions>(options => options.PlaybackHandleId, Target);
            registry.RegisterMember<VideoStopOptions>(options => options.HandleId, Target);
            registry.RegisterMember<InstanceIdOptions>(options => options.InstanceId, Target);
            registry.RegisterMember<EffectApplyOptions>(options => options.TargetHandleId, Target);
            registry.RegisterMember<CrossFadeOptions>(options => options.OldHandleId, Target);
            registry.RegisterMember<BlindsOptions>(options => options.OldHandleId, Target);
            registry.RegisterMember<SlideOptions>(options => options.FromLayerHandleId, Target);
            registry.RegisterMember<ColorFieldOptions>(options => options.FromLayerHandleId, Target);
            registry.RegisterMember<ColorFadeOptions>(options => options.FromLayerHandleId, Target);
        }
        return registry;
    }
}
