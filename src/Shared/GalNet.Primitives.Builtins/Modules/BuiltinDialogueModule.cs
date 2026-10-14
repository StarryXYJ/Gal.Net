using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.I18n;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinDialogueModule : PrimitiveModule
{
    public BuiltinDialogueModule(IDialoguePresenter? presenter = null, CharacterCatalog? characters = null) : base(
    [
        Text(presenter, characters ?? CharacterCatalog.Empty),
        Visibility(BuiltinEntryIds.ShowDialogue, presenter, true),
        Visibility(BuiltinEntryIds.HideDialogue, presenter, false)
    ])
    { }

    private static PrimitiveImplementation Text(IDialoguePresenter? presenter, CharacterCatalog characters) => PrimitiveImplementation.For<DialogueTextOptions>(BuiltinEntryIds.Text, (context, options) => new DialoguePrimitiveInstance(
            presenter ?? throw new InvalidOperationException("The dialogue module has no presenter."),
            ResolveSpeaker(context, characters, options.Speaker),
            context.Runtime.TextResolver.Resolve(options.Content ?? ""),
            context.BatchId,
            context.ScopeCancellation));

    private static string ResolveSpeaker(PrimitiveCreateContext context, CharacterCatalog characters, string? id)
    {
        return string.IsNullOrEmpty(id) ? "" : context.Runtime.TextResolver.Resolve(characters.GetRequired(id).DisplayNameKey);
    }

    private static PrimitiveImplementation Visibility(string type, IDialoguePresenter? presenter, bool visible)
        => PrimitiveImplementation.Immediate(type, _ =>
        {
            var target = presenter ?? throw new InvalidOperationException("The dialogue module has no presenter.");
            if (visible) target.ShowDialogue(); else target.HideDialogue();
        });
}
