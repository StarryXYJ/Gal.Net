using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using GalNet.Contracts.Builtins;
using GalNet.Core.Parameters;
using GalNet.Editor.MarkupExtensions;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>Editor-owned presentation extensions; value conversion and submission stay in the shared factory.</summary>
public static class EditorFieldEditors
{
    public static EditorRegistry CreateDefault(SceneTargetCatalog? scenes = null)
    {
        var registry = EditorRegistry.CreateDefault();
        registry.Replace(ValueEditorIds.Choice, context =>
        {
            var lease = DefaultFieldEditors.CreateChoice(context);
            ((ComboBox)lease.Control).ItemTemplate = new FuncDataTemplate<FieldEditorOption>((option, _) =>
            {
                var label = new TextBlock { DataContext = option?.Label };
                if (option?.Label is EntrySelectOption)
                {
                    var value = new LocalizeExtension("DisplayNameKey").ProvideValue(null!);
                    if (value is BindingBase binding) label.Bind(TextBlock.TextProperty, binding);
                    else label.Text = ((EntrySelectOption)option.Label).DisplayNameKey;
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
        }
        return registry;
    }
}
