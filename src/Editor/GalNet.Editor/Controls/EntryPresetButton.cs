using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Controls;

/// <summary>The same project preset popup is used by the Group row and Inspector.</summary>
public sealed class EntryPresetButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);
    public static readonly StyledProperty<GroupEditorPanelViewModel?> PageProperty = AvaloniaProperty.Register<EntryPresetButton, GroupEditorPanelViewModel?>(nameof(Page));
    public static readonly StyledProperty<EntryEditorItemViewModel?> EntryProperty = AvaloniaProperty.Register<EntryPresetButton, EntryEditorItemViewModel?>(nameof(Entry));
    public GroupEditorPanelViewModel? Page { get => GetValue(PageProperty); set => SetValue(PageProperty, value); }
    public EntryEditorItemViewModel? Entry { get => GetValue(EntryProperty); set => SetValue(EntryProperty, value); }
    private static string Text(string key, string fallback) => EditorLocalizationService.Current?[key] ?? fallback;

    protected override void OnClick()
    {
        base.OnClick();
        if (Page is not { } page || Entry is not { } entry || page.EntryPresets is not { } presets) return;
        var entryType = entry.Type;
        var panel = new StackPanel { Spacing = 6, MinWidth = 270, MaxWidth = 400 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock { Text = Text("Entry.Presets", "Presets") + " · " + entryType, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var add = new Button { Name = "AddEntryPreset", Content = "+" };
        ToolTip.SetTip(add, Text("Entry.PresetAdd", "Add preset"));
        Grid.SetColumn(add, 1); header.Children.Add(add);
        var list = new StackPanel { Spacing = 3 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed };
        panel.Children.Add(header);
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 320, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        panel.Children.Add(error);
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedLeft };
        FlyoutBase.SetAttachedFlyout(this, flyout);
        var busy = false;
        var gate = new SemaphoreSlim(1, 1);
        async Task Run(Func<Task> action)
        {
            await gate.WaitAsync();
            busy = true; error.Text = "";
            try
            {
                if (entry.Type != entryType) throw new InvalidOperationException("The entry type changed. Reopen presets.");
                await action();
            }
            catch (Exception failure) { error.Text = failure.Message; }
            finally { busy = false; gate.Release(); }
        }
        void Refresh(string? focus = null)
        {
            list.Children.Clear();
            foreach (var preset in presets.List(entryType))
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
                var name = new TextBox { Name = "EntryPresetName", Text = preset.Name, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(3), MinWidth = 120, MaxLength = 120, VerticalContentAlignment = VerticalAlignment.Center };
                var apply = new Button { Name = "ApplyEntryPreset", Content = Text("Entry.PresetApply", "Apply") };
                Grid.SetColumn(apply, 2); row.Children.Add(name);
                if (preset.IsBuiltin) { var label = new TextBlock { Text = Text("Entry.PresetBuiltin", "Built-in"), VerticalAlignment = VerticalAlignment.Center, Opacity = .7 }; Grid.SetColumn(label, 1); row.Children.Add(label); }
                row.Children.Add(apply);
                Task pendingRename = Task.CompletedTask;
                var renameFailed = false;
                ContextMenu CreateMenu()
                {
                    var menu = new ContextMenu();
                    var remove = new MenuItem { Header = Text("Entry.PresetDelete", "Delete preset") };
                    menu.Items.Add(remove);
                    remove.Click += async (_, _) => { await pendingRename; await Run(async () => { await presets.DeleteAsync(preset.Id); Refresh(); }); };
                    return menu;
                }
                if (!preset.IsBuiltin) { row.ContextMenu = CreateMenu(); name.ContextMenu = CreateMenu(); }
                apply.Click += async (_, _) => { await pendingRename; if (!renameFailed) await Run(async () => { await presets.ApplyAsync(page.GroupId, entry.Source, preset.Id); flyout.Hide(); }); };
                async Task CommitName()
                {
                    var value = name.Text?.Trim() ?? "";
                    if (value == preset.Name) { renameFailed = false; return; }
                    await Run(async () => { await presets.RenameAsync(preset.Id, value); Refresh(); });
                    renameFailed = !string.IsNullOrEmpty(error.Text);
                }
                name.LostFocus += async (_, _) => { pendingRename = CommitName(); await pendingRename; };
                name.KeyDown += async (_, args) =>
                {
                    if (args.Key is not (Key.Enter or Key.Escape) || name.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Any(item => !string.IsNullOrEmpty(item.PreeditText))) return;
                    args.Handled = true;
                    if (args.Key == Key.Escape) name.Text = preset.Name;
                    else { pendingRename = CommitName(); await pendingRename; }
                    apply.Focus();
                };
                list.Children.Add(row);
                if (preset.Id == focus) Dispatcher.UIThread.Post(() => { name.Focus(); name.SelectAll(); }, DispatcherPriority.Loaded);
            }
        }
        add.Click += async (_, _) => await Run(async () => { var preset = await presets.AddAsync(page.GroupId, entry.Source); Refresh(preset.Id); });
        void OnChanged() { if (!busy) { try { Refresh(); } catch (Exception failure) { error.Text = failure.Message; } } }
        presets.Changed += OnChanged;
        flyout.Closed += (_, _) => presets.Changed -= OnChanged;
        try { Refresh(); flyout.ShowAt(this); }
        catch (Exception failure) { presets.Changed -= OnChanged; page.ReportError(failure); }
    }
}
