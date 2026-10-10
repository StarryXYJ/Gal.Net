using System.Text.Json;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.I18n;
using GalNet.Core.Serialization;
using GalNet.Core.Services;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Loader;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public class GameTextLocalizationTests
{
    [Test]
    public void CompositeExpansionAndReorderingPreserveStoredContentKey()
    {
        var key = I18nKey.ForEntry("intro", "2", "content").Key;
        var raw = new GroupDocument
        {
            Entries =
            [
                new GroupEntryDocument
                {
                    Id = "2", Type = TextVoiceEntry.TypeId,
                    Parameters = new()
                    {
                        ["content"] = JsonSerializer.SerializeToElement(key),
                        ["asset"] = JsonSerializer.SerializeToElement("voice")
                    }
                }
            ]
        };
        var catalog = BuiltinSchemas.CreateCatalog();
        var first = GalgroupCompiler.Compile(raw, catalog).Document;
        raw.Entries.Insert(0, new GroupEntryDocument { Id = "1", Type = ShowDialogueEntry.TypeId });
        var second = GalgroupCompiler.Compile(raw, catalog).Document;
        var group = new Group();
        GalgroupLoader.LoadIntoGroupFromContent(group, JsonSerializer.Serialize(second));

        Assert.Multiple(() =>
        {
            Assert.That(first.Entries.Single(entry => entry.TypeId == TextEntry.TypeId).Arguments.GetProperty("content").GetString(), Is.EqualTo(key));
            Assert.That(((PrimitiveEntry)group.Entries[2]).Arguments.GetProperty("content").GetString(), Is.EqualTo(key));
            Assert.That(group.Entries[2].Id, Is.EqualTo(3));
        });
    }

    [Test]
    public void DialogueAndNvlResolveKeysAndCharacterNamesBeforePresentation()
    {
        var presenter = new TextPresenter();
        var characters = new CharacterCatalog([new("alice", "character.alice.name")]);
        using var view = new GameRegistration().AddBuiltinSchemas()
            .AddBuiltinDialogue(presenter, characters).AddBuiltinNvl(presenter).BuildView();
        var runtime = new GameRuntime(new TestResolver());
        view.Dispatch(new PrimitiveEntry(TextEntry.TypeId,
            JsonSerializer.SerializeToElement(new { speaker = "alice", content = "story.line" })), runtime, CancellationToken.None);
        view.Dispatch(new PrimitiveEntry(AppendNvlEntry.TypeId,
            JsonSerializer.SerializeToElement(new { content = "story.line" })), runtime, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(presenter.Speaker, Is.EqualTo("爱丽丝"));
            Assert.That(presenter.Dialogue, Is.EqualTo("你好"));
            Assert.That(presenter.Nvl, Is.EqualTo("你好"));
        });
        Assert.That(() => view.Dispatch(new PrimitiveEntry(TextEntry.TypeId,
            JsonSerializer.SerializeToElement(new { speaker = "unknown", content = "story.line" })), runtime, CancellationToken.None),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("unknown"));
    }

    private sealed class TestResolver : ITextResolver
    {
        public string Resolve(string key) => key switch
        {
            "character.alice.name" => "爱丽丝",
            "story.line" => "你好",
            _ => key
        };
    }

    private sealed class TextPresenter : IDialoguePresenter, INvlPresenter
    {
        public string Speaker { get; private set; } = "";
        public string Dialogue { get; private set; } = "";
        public string Nvl { get; private set; } = "";
        public Task PresentTextAsync(string speaker, string text, CancellationToken cancellationToken)
        {
            Speaker = speaker;
            Dialogue = text;
            return Task.CompletedTask;
        }
        public Task AppendNvlTextAsync(string text, CancellationToken cancellationToken)
        {
            Nvl = text;
            return Task.CompletedTask;
        }
        public void ShowDialogue() { }
        public void HideDialogue() { }
        public void SkipText() { }
        public void ShowNvl() { }
        public void HideNvl() { }
        public void ClearNvl() { }
        public void SkipNvlText() { }
    }
}
