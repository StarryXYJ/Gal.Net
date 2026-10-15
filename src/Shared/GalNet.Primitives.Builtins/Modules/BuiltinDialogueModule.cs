using GalNet.Contracts.Builtins;
using GalNet.Core.I18n;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinDialogueModule(IDialoguePresenter? presenter = null, CharacterCatalog? characters = null) : PrimitiveModule(
    registry =>
    {
        registry.Register<DialogueTextOptions>(BuiltinEntryIds.Text, (context, options) =>
            new DialoguePrimitiveInstance(presenter, characters ?? CharacterCatalog.Empty, context, options));
        Visibility(registry, BuiltinEntryIds.ShowDialogue, presenter, true);
        Visibility(registry, BuiltinEntryIds.HideDialogue, presenter, false);
    })
{
    private static void Visibility(PrimitiveRegistry registry, string type, IDialoguePresenter? presenter, bool visible)
        => registry.Immediate(type, _ =>
        {
            var target = presenter ?? throw new InvalidOperationException("The dialogue module has no presenter.");
            if (visible) target.ShowDialogue(); else target.HideDialogue();
        });
}
