using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinDialogueModule : PrimitiveModule
{
    public BuiltinDialogueModule(IDialoguePresenter? presenter = null) : base(
    [
        Text(presenter),
        Visibility<ShowDialogueEntry>(presenter, true),
        Visibility<HideDialogueEntry>(presenter, false)
    ])
    { }

    private static PrimitiveImplementation Text(IDialoguePresenter? presenter) => new PrimitiveImplementation(TextEntry.TypeId, context => new DialoguePrimitiveInstance(
            presenter ?? throw new InvalidOperationException("The dialogue module has no presenter."),
            Arguments.String(context, "speaker"),
            Arguments.String(context, "content"),
            context.BatchId,
            context.ScopeCancellation));

    private static PrimitiveImplementation Visibility<TEntry>(IDialoguePresenter? presenter, bool visible)
        where TEntry : PrimitiveEntry, new() => new PrimitiveImplementation(new TEntry().Type, context => new ImmediatePrimitiveInstance(() =>
        {
            var target = presenter ?? throw new InvalidOperationException("The dialogue module has no presenter.");
            if (visible) target.ShowDialogue(); else target.HideDialogue();
        }, batchId: context.BatchId));
}
