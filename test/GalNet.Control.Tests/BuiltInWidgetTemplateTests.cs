using GalNet.Control.Widget.BuiltIn;
using GalNet.Core.Widget;

namespace GalNet.Control.Tests;

public sealed class BuiltInWidgetTemplateTests
{
    [Test]
    public void DefaultDialogue_HasCorrectId()
    {
        var template = new DefaultDialogueTemplate();
        Assert.Multiple(() =>
        {
            Assert.That(template.Id, Is.EqualTo("DefaultDialogue"));
            Assert.That(template.Category, Is.EqualTo("DialogueBox"));
        });
    }

    [Test]
    public void DefaultDialogue_DefaultConfig_HasDefaults()
    {
        var template = new DefaultDialogueTemplate();
        var config = (DefaultDialogueConfig)template.CreateDefaultConfig();
        Assert.Multiple(() =>
        {
            Assert.That(config.Speaker, Is.Empty);
            Assert.That(config.FontSize, Is.EqualTo(16));
            Assert.That(config.TextSpeed, Is.EqualTo(40));
            Assert.That(config.BackgroundOpacity, Is.EqualTo(0.8));
        });
    }

    [Test]
    public void DefaultDialogue_CreateView_ReturnsNonNull()
    {
        var template = new DefaultDialogueTemplate();
        var config = template.CreateDefaultConfig();
        var view = template.CreateView(config);
        Assert.That(view, Is.Not.Null);
    }

    [Test]
    public void NvlDialogue_HasCorrectId()
    {
        var template = new NvlDialogueTemplate();
        Assert.That(template.Id, Is.EqualTo("NvlDialogue"));
    }

    [Test]
    public void NvlDialogue_CreateView_ReturnsNonNull()
    {
        var template = new NvlDialogueTemplate();
        var view = template.CreateView(template.CreateDefaultConfig());
        Assert.That(view, Is.Not.Null);
    }

    [Test]
    public void DefaultNvl_HasCorrectId()
    {
        var template = new DefaultNvlTemplate();
        Assert.That(template.Id, Is.EqualTo("DefaultNvl"));
        Assert.That(template.Category, Is.EqualTo("NvlBox"));
    }

    [Test]
    public void DefaultNvl_CreateView_ReturnsNonNull()
    {
        var template = new DefaultNvlTemplate();
        var view = template.CreateView(template.CreateDefaultConfig());
        Assert.That(view, Is.Not.Null);
    }

    [Test]
    public void DefaultChoice_HasCorrectId()
    {
        var template = new DefaultChoiceTemplate();
        Assert.That(template.Id, Is.EqualTo("DefaultChoice"));
        Assert.That(template.Category, Is.EqualTo("ChoicePanel"));
    }

    [Test]
    public void DefaultChoice_DefaultConfig_EmptyChoices()
    {
        var template = new DefaultChoiceTemplate();
        var config = (DefaultChoiceConfig)template.CreateDefaultConfig();
        Assert.That(config.Choices, Is.Empty);
    }

    [Test]
    public void DefaultChoice_CreateView_WithChoices_ReturnsPanel()
    {
        var template = new DefaultChoiceTemplate();
        var config = new DefaultChoiceConfig { Choices = ["选项A", "选项B", "选项C"] };
        var view = template.CreateView(config);
        Assert.That(view, Is.Not.Null);
    }

    [Test]
    public void HorizontalChoice_HasCorrectId()
    {
        var template = new HorizontalChoiceTemplate();
        Assert.That(template.Id, Is.EqualTo("HorizontalChoice"));
        Assert.That(template.Category, Is.EqualTo("ChoicePanel"));
    }

    [Test]
    public void HorizontalChoice_CreateView_ReturnsNonNull()
    {
        var template = new HorizontalChoiceTemplate();
        var view = template.CreateView(template.CreateDefaultConfig());
        Assert.That(view, Is.Not.Null);
    }

    [Test]
    public void DefaultButton_HasCorrectId()
    {
        var template = new DefaultButtonTemplate();
        Assert.That(template.Id, Is.EqualTo("DefaultButton"));
        Assert.That(template.Category, Is.EqualTo("Button"));
    }

    [Test]
    public void DefaultButton_DefaultConfig_TextIsButton()
    {
        var template = new DefaultButtonTemplate();
        var config = (DefaultButtonConfig)template.CreateDefaultConfig();
        Assert.That(config.Text, Is.EqualTo("Button"));
    }

    [Test]
    public void DefaultButton_CreateView_ReturnsNonNull()
    {
        var template = new DefaultButtonTemplate();
        var view = template.CreateView(template.CreateDefaultConfig());
        Assert.That(view, Is.Not.Null);
    }

    [Test]
    public void LargeButton_HasCorrectId()
    {
        var template = new LargeButtonTemplate();
        Assert.That(template.Id, Is.EqualTo("LargeButton"));
    }

    [Test]
    public void LargeButton_DefaultConfig_LargerSize()
    {
        var template = new LargeButtonTemplate();
        var config = (LargeButtonConfig)template.CreateDefaultConfig();
        Assert.That(config.Width, Is.EqualTo(300));
        Assert.That(config.Height, Is.EqualTo(60));
    }

    [Test]
    public void LargeButton_CreateView_ReturnsNonNull()
    {
        var template = new LargeButtonTemplate();
        var view = template.CreateView(template.CreateDefaultConfig());
        Assert.That(view, Is.Not.Null);
    }

    [Test]
    public void DefaultSlider_HasCorrectId()
    {
        var template = new DefaultSliderTemplate();
        Assert.That(template.Id, Is.EqualTo("DefaultSlider"));
        Assert.That(template.Category, Is.EqualTo("Slider"));
    }

    [Test]
    public void DefaultSlider_DefaultConfig_Range()
    {
        var template = new DefaultSliderTemplate();
        var config = (DefaultSliderConfig)template.CreateDefaultConfig();
        Assert.Multiple(() =>
        {
            Assert.That(config.Min, Is.EqualTo(0));
            Assert.That(config.Max, Is.EqualTo(100));
            Assert.That(config.Value, Is.EqualTo(50));
        });
    }

    [Test]
    public void DefaultSlider_CreateView_ReturnsNonNull()
    {
        var template = new DefaultSliderTemplate();
        var view = template.CreateView(template.CreateDefaultConfig());
        Assert.That(view, Is.Not.Null);
    }

    [Test]
    public void DefaultToggle_HasCorrectId()
    {
        var template = new DefaultToggleTemplate();
        Assert.That(template.Id, Is.EqualTo("DefaultToggle"));
        Assert.That(template.Category, Is.EqualTo("Toggle"));
    }

    [Test]
    public void DefaultToggle_CreateView_ReturnsNonNull()
    {
        var template = new DefaultToggleTemplate();
        var view = template.CreateView(template.CreateDefaultConfig());
        Assert.That(view, Is.Not.Null);
    }

    [Test]
    public void DefaultSlot_HasCorrectId()
    {
        var template = new DefaultSlotTemplate();
        Assert.That(template.Id, Is.EqualTo("DefaultSlot"));
        Assert.That(template.Category, Is.EqualTo("SaveSlot"));
    }

    [Test]
    public void DefaultSlot_DefaultConfig_IsEmpty()
    {
        var template = new DefaultSlotTemplate();
        var config = (DefaultSlotConfig)template.CreateDefaultConfig();
        Assert.That(config.IsEmpty, Is.True);
    }

    [Test]
    public void DefaultSlot_CreateView_ReturnsNonNull()
    {
        var template = new DefaultSlotTemplate();
        var view = template.CreateView(template.CreateDefaultConfig());
        Assert.That(view, Is.Not.Null);
    }

    [Test]
    public void DefaultTitleButton_HasCorrectId()
    {
        var template = new DefaultTitleButtonTemplate();
        Assert.That(template.Id, Is.EqualTo("DefaultTitleButton"));
        Assert.That(template.Category, Is.EqualTo("TitleButton"));
    }

    [Test]
    public void DefaultTitleButton_CreateView_ReturnsNonNull()
    {
        var template = new DefaultTitleButtonTemplate();
        var view = template.CreateView(template.CreateDefaultConfig());
        Assert.That(view, Is.Not.Null);
    }

    [Test]
    public void AllBuiltIn_DefaultConfigs_AreIndependent()
    {
        // 每次 CreateDefaultConfig 应返回新实例
        var template = new DefaultDialogueTemplate();
        var config1 = template.CreateDefaultConfig();
        var config2 = template.CreateDefaultConfig();
        Assert.That(config1, Is.Not.SameAs(config2));
    }
}
