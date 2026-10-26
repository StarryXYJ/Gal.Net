using GalNet.Contracts.Builtins.Gallery;
using GalNet.Contracts.Builtins.Assets;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using Dock.Model.Controls;
using Dock.Model.Core;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Dock;
using GalNet.Editor.History;
using GalNet.Editor.Models;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task DirectoryExpandersAreKnownWhenTheirParentOpensAndRefreshAfterChanges()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.Styles.Add(new Semi.Avalonia.SemiTheme());
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "TreeExpanders"), "TreeExpanders", new(), Contracts());
            Directory.CreateDirectory(Path.Combine(project.RootPath, "notes/empty"));
            Directory.CreateDirectory(Path.Combine(project.RootPath, "notes/files"));
            File.WriteAllText(Path.Combine(project.RootPath, "notes/files/a.txt"), "text");
            Directory.CreateDirectory(Path.Combine(project.RootPath, "notes/hidden/.galnet"));
            Directory.CreateDirectory(Path.Combine(project.RootPath, "notes/branch/leaf"));
            var vm = project.Services.GetRequiredService<AssetPanelViewModel>();
            var view = new AssetPanelView { DataContext = vm }; var window = new Window { Width = 1100, Height = 700, Content = view }; window.Show(); window.UpdateLayout();
            try
            {
                var notes = vm.Directories[0].Children.Single(node => node.Path == "notes"); notes.IsExpanded = true; window.UpdateLayout();
                foreach (var name in new[] { "empty", "files", "hidden" })
                {
                    var node = notes.Children.Single(child => child.Name == name);
                    Assert.That(node.IsExpanded, Is.False); Assert.That(node.Children, Is.Empty);
                    var item = view.GetVisualDescendants().OfType<TreeViewItem>().Single(item => ReferenceEquals(item.DataContext, node));
                    Assert.That(item.ItemCount, Is.Zero, "Visible leaf rows must already have no expander before clicking.");
                }
                var branch = notes.Children.Single(child => child.Name == "branch"); Assert.That(branch.IsExpanded, Is.False); Assert.That(branch.Children, Is.Not.Empty);
                branch.IsExpanded = true; window.UpdateLayout(); Assert.That(branch.Children.Single().Name, Is.EqualTo("leaf")); Assert.That(branch.Children.Single().Children, Is.Empty);
                Directory.CreateDirectory(Path.Combine(project.RootPath, "notes/empty/new")); vm.Refresh(); window.UpdateLayout();
                notes = vm.Directories[0].Children.Single(node => node.Path == "notes"); Assert.That(notes.IsExpanded, Is.True);
                Assert.That(notes.Children.Single(child => child.Name == "empty").Children, Is.Not.Empty);
                Directory.Delete(Path.Combine(project.RootPath, "notes/empty/new")); vm.Refresh(); window.UpdateLayout();
                notes = vm.Directories[0].Children.Single(node => node.Path == "notes"); Assert.That(notes.Children.Single(child => child.Name == "empty").Children, Is.Empty);
            }
            finally { window.Close(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task BrowserAddressBlocksDropAndGlobalSearchExitFollowSelection()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.Styles.Add(new Semi.Avalonia.SemiTheme());
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "BrowserAddress"), "BrowserAddress", new(), Contracts());
            var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
            await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "notes/nested/Match file.txt", Content: [65]));
            await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "other/MATCH file.txt", Content: [66]));
            var vm = project.Services.GetRequiredService<AssetPanelViewModel>(); vm.Navigate("notes/nested");
            var view = new AssetPanelView { DataContext = vm }; var window = new Window { Width = 1100, Height = 700, Content = view }; window.Show(); window.UpdateLayout();
            try
            {
                var address = view.FindControl<Border>("AddressBar")!;
                var point = address.TranslatePoint(new Point(address.Bounds.Width - 8, address.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var input = view.FindControl<TextBox>("PathInput")!;
                Assert.That(input.IsVisible, Is.True); input.Text = "other";
                input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); window.UpdateLayout();
                Assert.That(vm.CurrentDirectory, Is.EqualTo("other")); Assert.That(input.IsVisible, Is.False);
                vm.Navigate("notes/nested"); window.UpdateLayout();
                var crumb = address.GetVisualDescendants().OfType<Button>().Single(button => button.DataContext is FileBreadcrumb { Path: "notes" });
                point = crumb.TranslatePoint(new Point(8, 8), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.That(vm.CurrentDirectory, Is.EqualTo("notes")); Assert.That(vm.IsEditingPath, Is.False);
                vm.Navigate("notes/nested"); window.UpdateLayout();
                var target = address.GetVisualDescendants().OfType<Border>().Single(border => border.DataContext is FileBreadcrumb { Path: "notes" } && DragDrop.GetAllowDrop(border));
                var transfer = new DataTransfer(); transfer.Add(DataTransferItem.Create(DataFormat.CreateStringApplicationFormat("galnet.project-files"), System.Text.Json.JsonSerializer.Serialize(new { Root = vm.ProjectRoot, Paths = new[] { "notes/nested/Match file.txt" } })));
                target.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, target, new Point(3, 3), KeyModifiers.None));
                for (var attempt = 0; attempt < 40 && !File.Exists(Path.Combine(project.RootPath, "notes/Match file.txt")); attempt++) await Task.Delay(10);
                Assert.That(File.Exists(Path.Combine(project.RootPath, "notes/Match file.txt")), Is.True);
                await project.Services.GetRequiredService<EditorHistories>().Content.UndoAsync(); vm.Refresh();
                var search = view.FindControl<TextBox>("SearchInput")!; var clear = view.FindControl<Button>("ClearSearchButton")!;
                search.Text = "match file"; window.UpdateLayout(); Assert.That(vm.Entries, Has.Count.EqualTo(2)); Assert.That(clear.IsVisible, Is.True);
                vm.Select(vm.Entries.Single(item => item.Path.StartsWith("other/")));
                point = clear.TranslatePoint(new Point(10, 10), window)!.Value; window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.That(vm.SearchText, Is.Empty); Assert.That(vm.CurrentDirectory, Is.EqualTo("other")); Assert.That(vm.SelectedEntry?.Name, Is.EqualTo("MATCH file.txt")); Assert.That(clear.IsVisible, Is.False);
                search.Text = "no-match"; window.UpdateLayout(); search.Text = ""; window.UpdateLayout(); Assert.That(vm.CurrentDirectory, Is.EqualTo("other"));
                search.Text = "t:txt"; Assert.That(vm.Entries, Is.Empty, "Type syntax is reserved; current search matches literal filenames."); search.Text = "";
                var grid = view.FindControl<ListBox>("FileGrid")!;
                grid.ContextMenu!.Open(grid); window.UpdateLayout();
                var createMenu = grid.ContextMenu.Items.OfType<MenuItem>().Single(item => item.Items.OfType<MenuItem>().Any(child => child.Command == vm.NewFileCommand)); createMenu.IsSubMenuOpen = true; window.UpdateLayout();
                Assert.That(createMenu.Items.OfType<MenuItem>().Any(item => item.Command == vm.NewVariantsCommand), Is.True);
                grid.ContextMenu.Close();
                Assert.That(view.GetVisualDescendants().OfType<Button>().Any(button => button.Command == vm.ImportCommand || button.Command == vm.NewFolderCommand || button.Command == vm.NewFileCommand), Is.True, "Common creation actions are available without opening a context menu.");
                Assert.That(Grid.GetColumn(view.FindControl<Button>("RefreshButton")!), Is.EqualTo(2));
                Assert.That(grid.GetVisualDescendants().OfType<Border>().Any(border => Equals(ToolTip.GetTip(border), "MATCH file.txt")), Is.True);
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts) { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "p4b-browser-refined.png")); }
            }
            finally { window.Close(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public async Task TextFilesKeepEncodingIdentityAndSavedTextAcrossFileUndo(int encodingId)
    {
        Encoding encoding = encodingId switch { 0 => new UTF8Encoding(false, true), 1 => new UTF8Encoding(true, true), 2 => new UnicodeEncoding(false, true, true), 3 => new UnicodeEncoding(true, true, true), 4 => new UTF32Encoding(false, true, true), _ => new UTF32Encoding(true, true, true) };
        byte[] Bytes(string text) => encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Encoding"), "Encoding", new(), Contracts());
        var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
        var content = project.Services.GetRequiredService<IProjectContentSession>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var original = Bytes("first\r\n汉字😀\r\n");
        await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "notes/a.txt", Content: original)); history.Clear();
        var texts = project.Services.GetRequiredService<ProjectTextFiles>(); var buffer = texts.Open("notes/a.txt");
        var identity = content.FileDocuments[buffer.Id];
        Assert.That(content.IsDirty, Is.False);
        buffer.Text = "edited\r\n汉字😀\n"; await buffer.CommitAsync();
        Assert.That(content.IsDirty, Is.True);
        Assert.That(File.ReadAllBytes(Path.Combine(project.RootPath, "notes/a.txt")), Is.EqualTo(original));
        using (var view = new FileDocumentPanelViewModel(buffer)) Assert.That(view.Title, Does.EndWith("*"));
        Assert.That(texts.Open("notes/a.txt"), Is.SameAs(buffer), "Closing a view retains its project buffer.");
        await files.ExecuteAsync(new(ProjectFileAction.Move, ["notes/a.txt"], "notes/b.txt", false));
        Assert.That(buffer.Path, Is.EqualTo("notes/b.txt")); Assert.That(content.FileDocuments[buffer.Id], Is.SameAs(identity));
        Assert.That(File.Exists(Path.Combine(project.RootPath, "notes/a.txt")), Is.False);
        Assert.That(File.ReadAllBytes(Path.Combine(project.RootPath, "notes/b.txt")), Is.EqualTo(original));
        await content.SaveAsync(); var saved = Bytes(buffer.Text);
        Assert.That(File.ReadAllBytes(Path.Combine(project.RootPath, "notes/b.txt")), Is.EqualTo(saved));
        await history.UndoAsync();
        Assert.That(buffer.Path, Is.EqualTo("notes/a.txt")); Assert.That(File.ReadAllBytes(Path.Combine(project.RootPath, buffer.Path)), Is.EqualTo(saved));
        Assert.That(content.IsDirty, Is.False);
        await history.UndoAsync(); Assert.That(content.IsDirty, Is.True); Assert.That(buffer.Text, Is.EqualTo("first\r\n汉字😀\r\n"));
        await history.RedoAsync(); Assert.That(buffer.Text, Is.EqualTo("edited\r\n汉字😀\n")); Assert.That(content.IsDirty, Is.False);
        await projects.CloseAsync();
    }

    [Test]
    public async Task TextFileDeletionRestoresWorkingAndSavedVersionsAndExternalChangesAreProtected()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "TextDelete"), "TextDelete", new(), Contracts());
        var files = project.Services.GetRequiredService<ProjectFileAuthoringService>(); var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var content = project.Services.GetRequiredService<IProjectContentSession>();
        await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "notes/a.txt", Content: Encoding.UTF8.GetBytes("saved")));
        var buffer = project.Services.GetRequiredService<ProjectTextFiles>().Open("notes/a.txt"); buffer.Text = "working"; await buffer.CommitAsync();
        await files.ExecuteAsync(new(ProjectFileAction.Delete, [buffer.Path]));
        Assert.That(buffer.IsReadOnly, Is.True); Assert.That(File.Exists(Path.Combine(project.RootPath, buffer.Path)), Is.False);
        Assert.That(content.IsDirty, Is.False); await content.SaveAsync();
        await history.UndoAsync(); Assert.That(buffer.Text, Is.EqualTo("working")); Assert.That(File.ReadAllText(Path.Combine(project.RootPath, buffer.Path)), Is.EqualTo("saved"));
        File.WriteAllText(Path.Combine(project.RootPath, buffer.Path), "external"); buffer.CheckExternalChange(); Assert.That(buffer.Error, Is.Not.Empty);
        Assert.ThrowsAsync<EditorOperationException>(() => content.SaveAsync());
        Assert.That(File.ReadAllText(Path.Combine(project.RootPath, buffer.Path)), Is.EqualTo("external")); Assert.That(buffer.Text, Is.EqualTo("working"));
        await projects.CloseAsync();
    }

    [Test]
    public async Task BinaryUnknownEncodingAndLargeFilesAreReadOnlyWithoutDirtyingTheProject()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "ReadOnly"), "ReadOnly", new(), Contracts());
        var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
        await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "notes/binary.bin", Content: [65, 66]));
        await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "notes/invalid.txt", Content: [0xc0, 0xff, 0x80]));
        using (var stream = File.Create(Path.Combine(project.RootPath, "notes/large.txt"))) stream.SetLength(3 * 1024 * 1024);
        var texts = project.Services.GetRequiredService<ProjectTextFiles>();
        foreach (var name in new[] { "binary.bin", "invalid.txt", "large.txt" }) Assert.That(texts.Open("notes/" + name).IsReadOnly, Is.True);
        Assert.That(project.Services.GetRequiredService<IProjectContentSession>().IsDirty, Is.False);
        await projects.CloseAsync();
    }

    [Test]
    public async Task ProjectFileBrowserMountedControlsNavigateSelectRenameCopyAndConfirmDelete()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.Styles.Add(new Semi.Avalonia.SemiTheme());
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "Browser"), "Browser", new(), Contracts());
            var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
            await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "notes/a.txt", Content: [65]));
            await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "notes/b.txt", Content: [66]));
            await files.ExecuteAsync(new(ProjectFileAction.CreateDirectory, [], "notes/child"));
            var vm = project.Services.GetRequiredService<AssetPanelViewModel>();
            var view = new AssetPanelView { DataContext = vm }; var window = new Window { Width = 1100, Height = 700, Content = view }; window.Show(); window.UpdateLayout();
            try
            {
                var tree = view.FindControl<TreeView>("DirectoryTree")!; var grid = view.FindControl<ListBox>("FileGrid")!;
                Assert.That(tree.GetVisualDescendants().OfType<TreeViewItem>(), Is.Not.Empty);
                Assert.That(vm.Entries.Any(item => item.Path == ".galnet" || item.Path.EndsWith(".meta")), Is.False);
                Assert.That(vm.Entries.Single(item => item.Path == "Contracts").CanEdit, Is.False);
                var notes = vm.Directories[0].Children.Single(node => node.Path == "notes"); tree.SelectedItem = notes; window.UpdateLayout();
                Assert.That(vm.CurrentDirectory, Is.EqualTo("notes")); Assert.That(vm.Entries, Has.Count.EqualTo(3));
                var count = project.Services.GetRequiredService<EditorHistories>().Content.UndoDescription;
                vm.ThumbnailSize = 120; vm.SearchText = "a.txt"; Assert.That(vm.Entries.Single().Name, Is.EqualTo("a.txt")); vm.SearchText = "";
                Assert.That(project.Services.GetRequiredService<EditorHistories>().Content.UndoDescription, Is.EqualTo(count));
                grid.SelectedItem = vm.Entries.Single(item => item.Name == "a.txt"); window.UpdateLayout(); grid.Focus();
                grid.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F2 }); window.UpdateLayout();
                Assert.That(vm.RenamingEntry?.Name, Is.EqualTo("a.txt"));
                var rename = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.IsVisible && box.DataContext is ProjectFileItem { IsRenaming: true });
                rename.Text = "renamed.txt"; await vm.CommitRenameAsync(); window.UpdateLayout();
                Assert.That(File.Exists(Path.Combine(project.RootPath, "notes/renamed.txt")), Is.True);
                window.UpdateLayout();
                var first = grid.GetVisualDescendants().OfType<ListBoxItem>().Single(item => item.DataContext is ProjectFileItem { Name: "renamed.txt" });
                var second = grid.GetVisualDescendants().OfType<ListBoxItem>().Single(item => item.DataContext is ProjectFileItem { Name: "b.txt" });
                var point = first.TranslatePoint(new Point(12, 12), window)!.Value; window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
                window.UpdateLayout(); second = grid.GetVisualDescendants().OfType<ListBoxItem>().Single(item => item.DataContext is ProjectFileItem { Name: "b.txt" });
                point = second.TranslatePoint(new Point(12, 12), window)!.Value; window.MouseDown(point, MouseButton.Left, RawInputModifiers.Control); window.MouseUp(point, MouseButton.Left, RawInputModifiers.Control);
                Assert.That(vm.SelectedEntries, Has.Count.EqualTo(2)); vm.CopySelectedCommand.Execute(null); vm.Navigate("notes/child"); await vm.PasteCommand.ExecuteAsync(null);
                Assert.That(vm.Entries.Select(item => item.Name), Is.EquivalentTo(new[] { "renamed.txt", "b.txt" }));
                grid.SelectAll(); await vm.DeleteSelectedCommand.ExecuteAsync(null); Assert.That(vm.IsDeletePending, Is.True);
                vm.CancelDeleteCommand.Execute(null); Assert.That(vm.Entries, Has.Count.EqualTo(2));
                await vm.DeleteSelectedCommand.ExecuteAsync(null); await vm.ConfirmDeleteCommand.ExecuteAsync(null); Assert.That(vm.Entries, Is.Empty);
                await project.Services.GetRequiredService<EditorHistories>().Content.UndoAsync(); vm.Refresh(); Assert.That(vm.Entries, Has.Count.EqualTo(2));
                vm.BackCommand.Execute(null); Assert.That(vm.CurrentDirectory, Is.EqualTo("notes")); vm.ForwardCommand.Execute(null); Assert.That(vm.CurrentDirectory, Is.EqualTo("notes/child"));
                await files.ExecuteAsync(new(ProjectFileAction.CreateDirectory, [], "notes/moved")); vm.Navigate("notes/moved");
                var transfer = new DataTransfer(); transfer.Add(DataTransferItem.Create(DataFormat.CreateStringApplicationFormat("galnet.project-files"), System.Text.Json.JsonSerializer.Serialize(new { Root = vm.ProjectRoot, Paths = new[] { "notes/child/b.txt" } })));
                grid.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, grid, new Point(20, 20), KeyModifiers.None));
                for (var attempt = 0; attempt < 30 && !File.Exists(Path.Combine(project.RootPath, "notes/moved/b.txt")); attempt++) await Task.Delay(10);
                Assert.That(File.Exists(Path.Combine(project.RootPath, "notes/moved/b.txt")), Is.True);
                Assert.That(File.Exists(Path.Combine(project.RootPath, "notes/child/b.txt")), Is.False);
                await project.Services.GetRequiredService<EditorHistories>().Content.UndoAsync(); vm.Navigate("notes");
                vm.Navigate("../outside");
                var content = project.Services.GetRequiredService<IProjectContentSession>(); var snapshot = System.Text.Json.JsonSerializer.Serialize(content.CaptureWorking());
                var historyPosition = project.Services.GetRequiredService<EditorHistories>().Content.UndoDescription;
                var localization = services.GetRequiredService<IEditorLocalizationService>();
                foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
                {
                    localization.ApplyLocale(locale); window.UpdateLayout();
                    Assert.That(view.FindControl<TextBox>("SearchInput")!.PlaceholderText, Is.EqualTo(localization["Files.Search"]));
                    Assert.That(vm.ErrorText, Is.EqualTo(localization.Format("Error.files.path", "../outside")));
                    Assert.That(System.Text.Json.JsonSerializer.Serialize(content.CaptureWorking()), Is.EqualTo(snapshot));
                    Assert.That(project.Services.GetRequiredService<EditorHistories>().Content.UndoDescription, Is.EqualTo(historyPosition));
                }
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts) { Directory.CreateDirectory(artifacts); using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "p4b-project-files.png")); }
            }
            finally { window.Close(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task TextPageBindsRealInputAndDockTitleFollowsTheSameBuffer()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.Styles.Add(new Semi.Avalonia.SemiTheme());
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "TextView"), "TextView", new(), Contracts());
            var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
            await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "notes/a.txt", Content: Encoding.UTF8.GetBytes("before\r\n")));
            var texts = project.Services.GetRequiredService<ProjectTextFiles>(); var buffer = texts.Open("notes/a.txt");
            var dock = project.Services.GetRequiredService<EditorDockFactory>(); var layout = dock.CreateLayout(); dock.InitLayout(layout); dock.OpenFileEditor(buffer); dock.OpenFileEditor(buffer);
            var documents = DockDocuments(layout).Where(item => item.Id == "FileEditor:" + buffer.Id).ToArray(); Assert.That(documents, Has.Length.EqualTo(1));
            using var pane = new FileDocumentPanelViewModel(buffer); var view = new FileDocumentPanelView { DataContext = pane };
            var window = new Window { Width = 800, Height = 600, Content = view }; window.Show(); window.UpdateLayout();
            try
            {
                view.FindControl<TextBox>("FileTextInput")!.Text = "after\r\n"; await buffer.CommitAsync();
                Assert.That(documents.Single().Title, Does.EndWith("*"));
                await files.ExecuteAsync(new(ProjectFileAction.Move, [buffer.Path], "notes/renamed.txt", false));
                Assert.That(documents.Single().Title, Does.StartWith("renamed.txt")); await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
                Assert.That(File.ReadAllText(Path.Combine(project.RootPath, buffer.Path)), Is.EqualTo("after\r\n"));
            }
            finally { window.Close(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
    private static IEnumerable<IDockable> DockDocuments(IDockable item) => new[] { item }.Concat(item is IDock dock && dock.VisibleDockables is not null ? dock.VisibleDockables.SelectMany(DockDocuments) : []);

    [Test]
    public async Task TextSaveFailureAfterWritingBytesRestoresDiskAndCanRetry()
    {
        var fault = new TextPublishFailure(); using var services = Services(transaction: fault); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "TextFault"), "TextFault", new(), Contracts());
        try
        {
            await project.Services.GetRequiredService<ProjectFileAuthoringService>().ExecuteAsync(new(ProjectFileAction.CreateFile, [], "notes/a.txt", Content: Encoding.UTF8.GetBytes("old")));
            var buffer = project.Services.GetRequiredService<ProjectTextFiles>().Open("notes/a.txt"); buffer.Text = "new"; await buffer.CommitAsync();
            var content = project.Services.GetRequiredService<IProjectContentSession>(); var saved = System.Text.Json.JsonSerializer.Serialize(content.CaptureSaved());
            fault.Armed = true; Assert.ThrowsAsync<IOException>(() => content.SaveAsync());
            Assert.That(File.ReadAllText(Path.Combine(project.RootPath, buffer.Path)), Is.EqualTo("old")); Assert.That(buffer.Text, Is.EqualTo("new"));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(content.CaptureSaved()), Is.EqualTo(saved));
            await content.SaveAsync(); Assert.That(File.ReadAllText(Path.Combine(project.RootPath, buffer.Path)), Is.EqualTo("new"));
        }
        finally { await projects.CloseAsync(); }
    }

    [Test]
    public async Task ProjectRootImportAndDuplicateUseFreshAssetIdsAndUniqueNames()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Copy"), "Copy", new(), Contracts());
        try
        {
            var files = project.Services.GetRequiredService<ProjectFileAuthoringService>(); var external = Path.Combine(_root, "import.txt"); File.WriteAllText(external, "import");
            await files.ExecuteAsync(new(ProjectFileAction.Import, [external], "", UniqueNames: true)); Assert.That(File.ReadAllText(Path.Combine(project.RootPath, "import.txt")), Is.EqualTo("import"));
            await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "Assets/image.png", Content: [1, 2]));
            File.WriteAllText(Path.Combine(project.RootPath, "Assets/image.png.meta"), """{"id":"original","type":"sprite","path":"image.png","gallery":[{"id":17,"typeId":"cg","title":"Picture"}],"custom":"retained"}""");
            await files.ExecuteAsync(new(ProjectFileAction.Copy, ["Assets/image.png"], "Assets", UniqueNames: true));
            var codec = new GalNet.Core.Assets.AssetMetaCodec(GalNet.Contracts.Builtins.Assets.BuiltinResourceTypes.CreateCatalog());
            var original = codec.Deserialize(File.ReadAllText(Path.Combine(project.RootPath, "Assets/image.png.meta")));
            var copy = codec.Deserialize(File.ReadAllText(Path.Combine(project.RootPath, "Assets/image 1.png.meta")));
            Assert.That(copy.Id, Is.Not.EqualTo(original.Id)); Assert.That(copy.Path, Is.EqualTo("image 1.png"));
            Assert.That(copy.ReadGallery().Single().Id, Is.Not.EqualTo(original.ReadGallery().Single().Id)); Assert.That(copy.ReadGallery().Single().Title, Is.EqualTo("Picture")); Assert.That(copy.ExtensionData!["custom"].GetString(), Is.EqualTo("retained"));
            await project.Services.GetRequiredService<EditorHistories>().Content.UndoAsync(); Assert.That(File.Exists(Path.Combine(project.RootPath, "Assets/image 1.png")), Is.False);
        }
        finally { await projects.CloseAsync(); }
    }

    [Test]
    public async Task OpeningNewTextBufferDuringSaveCannotLoseItsBaseline()
    {
        var pause = new TextPublishPause(); using var services = Services(transaction: pause); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Opening"), "Opening", new(), Contracts());
        try
        {
            var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
            await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "notes/a.txt", Content: [65]));
            var content = project.Services.GetRequiredService<IProjectContentSession>(); var texts = project.Services.GetRequiredService<ProjectTextFiles>();
            pause.Armed = true; var save = content.SaveAsync();
            try { await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); var error = Assert.Throws<EditorOperationException>(() => texts.Open("notes/a.txt")); Assert.That(error!.Code, Is.EqualTo("files.busy")); }
            finally { pause.Continue.TrySetResult(); await save; }
            Assert.That(texts.Open("notes/a.txt").IsDirty, Is.False); Assert.That(content.IsDirty, Is.False);
        }
        finally { pause.Continue.TrySetResult(); await projects.CloseAsync(); }
    }
    [Test]
    public async Task OpenTextFileMoveUndoCannotOverwriteAnExternallyOccupiedTarget()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Occupied"), "Occupied", new(), Contracts());
        try
        {
            var files = project.Services.GetRequiredService<ProjectFileAuthoringService>(); var history = project.Services.GetRequiredService<EditorHistories>().Content;
            await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "notes/a.txt", Content: Encoding.UTF8.GetBytes("old")));
            var buffer = project.Services.GetRequiredService<ProjectTextFiles>().Open("notes/a.txt"); buffer.Text = "new"; await buffer.CommitAsync();
            await files.ExecuteAsync(new(ProjectFileAction.Move, [buffer.Path], "notes/b.txt", false)); await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
            File.WriteAllText(Path.Combine(project.RootPath, "notes/a.txt"), "external");
            Assert.ThrowsAsync<EditorOperationException>(() => history.UndoAsync());
            Assert.That(buffer.Path, Is.EqualTo("notes/b.txt")); Assert.That(File.ReadAllText(Path.Combine(project.RootPath, "notes/a.txt")), Is.EqualTo("external"));
            File.Delete(Path.Combine(project.RootPath, "notes/a.txt")); await history.UndoAsync();
            Assert.That(File.ReadAllText(Path.Combine(project.RootPath, "notes/a.txt")), Is.EqualTo("new"));
        }
        finally { await projects.CloseAsync(); }
    }

    [Test]
    public async Task SelectedAndLockedAssetInspectorFollowIdentityThroughRenameDeleteAndUndo()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "AssetIdentity"), "AssetIdentity", new(), Contracts());
            try
            {
                var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
                await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "Assets/info.txt", Content: [65]));
                var browser = project.Services.GetRequiredService<AssetPanelViewModel>(); browser.Navigate("Assets"); browser.Select(browser.Entries.Single(item => item.Name == "info.txt"));
                using var inspector = project.Services.GetRequiredService<GalNet.Editor.Inspector.ViewModels.AssetInspectorControlViewModel>();
                var id = inspector.InspectedAsset!.Id; inspector.SetLocked(true);
                browser.BeginRenameCommand.Execute(browser.SelectedEntry); browser.RenameText = "renamed.txt"; await browser.CommitRenameAsync();
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.That(browser.SelectedEntry?.Path, Is.EqualTo("Assets/renamed.txt"));
                Assert.That(inspector.InspectedAsset?.Id, Is.EqualTo(id)); Assert.That(inspector.InspectedAsset?.RelativePath, Is.EqualTo("renamed.txt"));
                await files.ExecuteAsync(new(ProjectFileAction.Delete, ["Assets/renamed.txt"]));
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.That(inspector.InspectedAsset, Is.Null);
                await project.Services.GetRequiredService<EditorHistories>().Content.UndoAsync();
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.That(inspector.InspectedAsset?.Id, Is.EqualTo(id));
                await project.Services.GetRequiredService<EditorHistories>().Content.UndoAsync();
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.That(inspector.InspectedAsset?.RelativePath, Is.EqualTo("info.txt"));
            }
            finally { await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AssetDeletionChecksUnsavedTextReferencesInsteadOfStaleDiskContent(bool utf16)
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "DraftReference"), "DraftReference", new(), Contracts());
        try
        {
            var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
            await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "Assets/image.png", Content: [1, 2]));
            var meta = new GalNet.Core.Assets.AssetMetaCodec(GalNet.Contracts.Builtins.Assets.BuiltinResourceTypes.CreateCatalog()).Deserialize(File.ReadAllText(Path.Combine(project.RootPath, "Assets/image.png.meta")));
            Encoding encoding = utf16 ? new UnicodeEncoding(true, true, true) : new UTF8Encoding(false, true);
            var original = encoding.GetPreamble().Concat(encoding.GetBytes("{}")).ToArray();
            await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "UI/ui.json", Content: original));
            var buffer = project.Services.GetRequiredService<ProjectTextFiles>().Open("UI/ui.json"); buffer.Text = "{\"image\":\"" + meta.Id + "\"}";
            var error = Assert.ThrowsAsync<EditorOperationException>(() => files.ExecuteAsync(new(ProjectFileAction.Delete, ["Assets/image.png"])));
            Assert.That(error!.Code, Is.EqualTo("files.referenced")); Assert.That(File.Exists(Path.Combine(project.RootPath, "Assets/image.png")), Is.True);
            Assert.That(File.ReadAllBytes(Path.Combine(project.RootPath, "UI/ui.json")), Is.EqualTo(original));
            var content = project.Services.GetRequiredService<IProjectContentSession>();
            var impact = files.Analyze(new(ProjectFileAction.Delete, ["Assets/image.png"]));
            Assert.That(impact.References.Single().Document, Is.EqualTo("UI/ui.json")); Assert.That(impact.References.Single().Certain, Is.False);
            await files.ExecuteAsync(new(ProjectFileAction.Delete, ["Assets/image.png"], AllowCandidateReferences: true));
            await project.Services.GetRequiredService<EditorHistories>().Content.UndoAsync();
            Assert.That(buffer.Text, Does.Contain(meta.Id)); Assert.That(content.IsDirty, Is.True);
        }
        finally { await projects.CloseAsync(); }
    }

    private sealed class TextPublishFailure : GalNet.Editor.Shared.Commands.AuthorFileTransaction
    {
        public bool Armed;
        protected override void Publish(string path, string? stagedFile)
        {
            base.Publish(path, stagedFile);
            if (Armed && path.EndsWith(".txt", StringComparison.Ordinal)) { Armed = false; throw new IOException("Injected failure after publishing text"); }
        }
    }
    private sealed class TextPublishPause : GalNet.Editor.Shared.Commands.AuthorFileTransaction
    {
        public bool Armed;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override void Publish(string path, string? stagedFile)
        {
            if (Armed) { Armed = false; Started.TrySetResult(); Continue.Task.GetAwaiter().GetResult(); }
            base.Publish(path, stagedFile);
        }
    }
}
