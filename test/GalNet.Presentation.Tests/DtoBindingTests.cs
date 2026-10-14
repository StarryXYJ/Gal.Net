using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Core.Parameters;
using GalNet.Game.Controls.Forms;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class DtoBindingTests
{
    [Test]
    public async Task GeneratedMembersBindInOrderAndObservablePropertyUpdatesAutomatically()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var data = new ObservableFormDto();
            using var binding = new DtoBinding(data);
            var form = Form(binding);
            var window = Mount(form);
            try
            {
                Assert.That(form.Contexts.Select(context => context.Field.Id), Is.EqualTo(new[] { "count", "name" }));
                var input = (TextBox)form.Editors[0].Control;
                input.Text = "11";
                Assert.That(await form.Contexts[0].CommitTextAsync(), Is.False);
                Assert.That(data.Count, Is.Zero);
                input.Text = "4";
                Assert.That(await form.Contexts[0].CommitTextAsync(), Is.True);
                Assert.That(data.Count, Is.EqualTo(4));
                data.Count = 7;
                Assert.That(input.Text, Is.EqualTo("7"));
                ((TextBox)form.Editors[1].Control).Text = "renamed";
                Assert.That(await form.Contexts[1].CommitTextAsync(), Is.True);
                Assert.That(data.Name, Is.EqualTo("renamed"));
                Assert.That(JsonSerializer.Serialize(data), Does.Contain("renamed"));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task PlainDtoRefreshPreservesConverterDefaultsAndNullAndDoesNotShareValues()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var data = new PlainFormDto();
            using var binding = new DtoBinding(data);
            using var other = new DtoBinding(new PlainFormDto());
            var form = Form(binding);
            var window = Mount(form);
            try
            {
                Assert.That(form.Contexts[0].Field.DefaultValue!.Value.GetInt32(), Is.EqualTo(2));
                Assert.That(form.Contexts[1].Value.Json!.Value.ValueKind, Is.EqualTo(JsonValueKind.Null));
                Assert.That(form.Contexts[2].Options.Any(option => option.Value.GetString() == "Monday"), Is.True);
                Assert.That(await form.Contexts[2].CommitValueAsync(DayOfWeek.Friday), Is.True);
                Assert.That(data.Day, Is.EqualTo(DayOfWeek.Friday));
                Assert.That(other.Data.ReadField("date"), Is.EqualTo(DayOfWeek.Monday));
                data.Count = 5;
                Assert.That(((TextBox)form.Editors[0].Control).Text, Is.EqualTo("2"));
                binding.Refresh();
                Assert.That(((TextBox)form.Editors[0].Control).Text, Is.EqualTo("5"));
                Assert.That(await form.Contexts[1].CommitAsync(new(JsonSerializer.SerializeToElement<string?>(null))), Is.True);
                Assert.That(data.Name, Is.Null);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task HostOwnsSubmissionAndDetachUnsubscribesFromTheBorrowedDto()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var data = new CountedFormDto();
            var started = new TaskCompletionSource();
            using var binding = new DtoBinding(data, async (_, _, token) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return null;
            });
            var form = Form(binding);
            var window = Mount(form);
            Assert.That(data.Subscribers, Is.EqualTo(1));
            var context = form.Contexts[0];
            context.Text = "3";
            var edit = context.CommitTextAsync();
            await started.Task;
            window.Content = null;
            Assert.That(await edit, Is.False);
            Assert.That(data.Subscribers, Is.Zero);
            Assert.That(data.Count, Is.Zero);
            var member = binding.Binding(binding.Fields[0]);
            binding.Dispose();
            Assert.That(await member.CommitAsync(new(JsonSerializer.SerializeToElement(4)), CancellationToken.None), Is.Not.Null);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static SchemaForm Form(DtoBinding binding) => new() { Schema = binding.Fields, Binding = binding.Binding, Registry = EditorRegistry.CreateDefault() };
    private static Window Mount(SchemaForm form)
    {
        var window = new Window { Content = form, Width = 500, Height = 400 };
        window.Styles.Add(new FluentTheme());
        window.Styles.Add(new StyleInclude(new Uri("avares://GalNet.Avalonia.Controls/")) { Source = new Uri("avares://GalNet.Avalonia.Controls/Styles/SchemaForm.axaml") });
        window.Show();
        window.UpdateLayout();
        Assert.That(form.Diagnostics, Is.Empty);
        return window;
    }
}

[GenerateFieldSchema]
public partial class ObservableFormDto : ObservableObject
{
    [Field(0, Minimum = 0, Maximum = 10)]
    [ObservableProperty]
    public partial int Count { get; set; }

    [Field(1)]
    [ObservableProperty]
    public partial string? Name { get; set; }
}

[GenerateFieldSchema]
public partial class PlainFormDto
{
    [Field(0, Minimum = 0, Maximum = 10)] public int Count { get; set; } = 2;
    [Field(1, AllowNull = true, EditorId = "json")] public string? Name { get; set; } = null;
    [Field(2), JsonPropertyName("date"), JsonConverter(typeof(JsonStringEnumConverter))]
    public DayOfWeek Day { get; set; } = DayOfWeek.Monday;
}

[GenerateFieldSchema]
public partial class CountedFormDto : INotifyPropertyChanged
{
    private PropertyChangedEventHandler? _changed;
    public int Subscribers { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { _changed += value; Subscribers++; }
        remove { _changed -= value; Subscribers--; }
    }
    private int _count;
    [Field(0)] public int Count { get => _count; set { _count = value; _changed?.Invoke(this, new(nameof(Count))); } }
}
