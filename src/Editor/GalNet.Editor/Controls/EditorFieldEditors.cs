using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using GalNet.Contracts.Builtins;
using GalNet.Core.Parameters;
using GalNet.Core.Scene;
using GalNet.Editor.MarkupExtensions;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>Editor-owned presentation extensions; value conversion and submission stay in the shared factory.</summary>
public static class EditorFieldEditors
{
    public static EditorRegistry CreateDefault(SceneTargetCatalog? scenes = null, Action<FieldEditorContext>? openTimeline = null, GalNet.Core.Assets.IAssetManager? assets = null, CharacterAuthoringService? characters = null)
    {
        var registry = EditorRegistry.CreateDefault(emptyInputClearsOptionalFields: true);
        if (characters is not null) registry.RegisterSemantic(FieldValueSemantic.CharacterReference, context => CharacterFieldEditor.Create(context, characters));
        registry.Register("entry.imageasset", context => AssetFieldEditor.Create(context, assets, Models.AssetPickerFilter.Image));
        registry.Register("entry.effectprogramasset", context => AssetFieldEditor.Create(context, assets, Models.AssetPickerFilter.EffectProgram));
        registry.RegisterDefault<LayerTransform>(TransformFieldEditor.Create);
        registry.RegisterMember<LayerShowOptions>(value => value.Transform, TransformFieldEditor.Create);
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
            FieldEditorLease Target(FieldEditorContext context) => SceneTargetFieldEditor.Create(context, scenes);
            registry.RegisterMember<LayerHandleOptions>(options => options.HandleId, Target);
            registry.RegisterMember<LayerMoveOptions>(options => options.HandleId, Target);
            registry.RegisterMember<LayerReplaceOptions>(options => options.HandleId, Target);
            registry.RegisterMember<AnimationOptions>(options => options.HandleId, Target);
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
