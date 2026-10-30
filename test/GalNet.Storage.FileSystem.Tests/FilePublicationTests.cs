using System.Runtime.InteropServices;
using GalNet.Storage.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace GalNet.Storage.FileSystem.Tests;

[NonParallelizable]
public sealed class FilePublicationTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "galnet-publish-" + Guid.NewGuid().ToString("N")));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task Existing_destination_is_not_replaced_by_directory_publication()
    {
        var source = CreateSource();
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "original.txt"), "original");
        Assert.ThrowsAsync<IOException>(() => FilePublication.MoveDirectoryAsync(source, target));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(target, "original.txt")), Is.EqualTo("original"));
        Assert.That(File.Exists(Path.Combine(source, "content.txt")), Is.True);
    }

    [Test]
    public async Task Cancelled_publication_keeps_the_source()
    {
        var source = CreateSource();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(() => FilePublication.MoveDirectoryAsync(source, Path.Combine(_root, "target"), cancellation.Token));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(source, "content.txt")), Is.EqualTo("content"));
    }

    [Test]
    public async Task Directory_publication_waits_for_a_temporary_windows_handle()
    {
        RequireWindows();
        var source = CreateSource();
        var target = Path.Combine(_root, "target");
        using var handle = HoldDirectory(source);
        var move = FilePublication.MoveDirectoryAsync(source, target);
        Assert.That(move.IsCompleted, Is.False, "An open handle without delete sharing must block the initial rename.");
        await Task.Delay(60);
        handle.Dispose();
        await move.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(target, "content.txt")), Is.EqualTo("content"));
        Assert.That(Directory.Exists(source), Is.False);
    }

    [Test]
    public void Persistent_windows_handle_fails_without_publishing_partial_content()
    {
        RequireWindows();
        var source = CreateSource();
        var target = Path.Combine(_root, "target");
        using var handle = HoldDirectory(source);
        Assert.CatchAsync<IOException>(() => FilePublication.MoveDirectoryAsync(source, target));
        Assert.That(Directory.Exists(source), Is.True);
        Assert.That(Directory.Exists(target), Is.False);
    }

    [Test]
    public void Cancellation_during_windows_contention_keeps_the_source()
    {
        RequireWindows();
        var source = CreateSource();
        var target = Path.Combine(_root, "target");
        using var handle = HoldDirectory(source);
        using var cancellation = new CancellationTokenSource();
        var move = FilePublication.MoveDirectoryAsync(source, target, cancellation.Token);
        Assert.That(move.IsCompleted, Is.False);
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await move);
        Assert.That(Directory.Exists(source), Is.True);
        Assert.That(Directory.Exists(target), Is.False);
    }

    [Test]
    public async Task File_replacement_waits_without_deleting_the_previous_version()
    {
        RequireWindows();
        var source = Path.Combine(_root, "temporary.json");
        var target = Path.Combine(_root, "profile.json");
        await File.WriteAllTextAsync(source, "new");
        await File.WriteAllTextAsync(target, "previous");
        using var handle = File.Open(target, FileMode.Open, FileAccess.Read, FileShare.Read);
        var move = FilePublication.MoveFileAsync(source, target, overwrite: true);
        Assert.That(move.IsCompleted, Is.False);
        using (var reader = new StreamReader(handle, leaveOpen: true))
            Assert.That(await reader.ReadToEndAsync(), Is.EqualTo("previous"));
        await Task.Delay(60);
        handle.Dispose();
        await move.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(await File.ReadAllTextAsync(target), Is.EqualTo("new"));
        Assert.That(File.Exists(source), Is.False);
    }

    private string CreateSource()
    {
        var source = Path.Combine(_root, "stage");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "content.txt"), "content");
        return source;
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("This test exercises Windows delete-sharing semantics.");
    }

    private static SafeFileHandle HoldDirectory(string path)
    {
        var handle = CreateFile(path, 0x80000000, 3, 0, 3, 0x02000000, 0);
        Assert.That(handle.IsInvalid, Is.False, $"CreateFile failed: {Marshal.GetLastWin32Error()}");
        return handle;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
}
