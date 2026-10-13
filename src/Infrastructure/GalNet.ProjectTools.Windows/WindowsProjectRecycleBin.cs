using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GalNet.Editor.Abstraction.Commands;

namespace GalNet.ProjectTools.Windows;

/// <summary>Windows Shell recycling only. There is no permanent-delete path.</summary>
public sealed class WindowsProjectRecycleBin : IProjectRecycleBin
{
    [SupportedOSPlatformGuard("windows6.2")]
    public bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(6, 2);

    public Task RecycleAsync(string projectDirectory, CancellationToken cancellationToken = default)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Project recycling requires Windows 8 or newer.");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectDirectory));
        var driveRoot = Path.GetPathRoot(path)!;
        if (path == driveRoot || path.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(driveRoot).DriveType != DriveType.Fixed)
            throw new NotSupportedException("Only project directories on local fixed drives can be recycled.");
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Recycle paths cannot traverse links.");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                cancellationToken.ThrowIfCancellationRequested(); Recycle(path, cancellationToken); completion.SetResult();
            }
            catch (OperationCanceledException) { completion.SetCanceled(cancellationToken); }
            catch (Exception exception) { completion.SetException(exception); }
        })
        { IsBackground = true, Name = "GalNet project recycle" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // Once the Shell operation starts, wait for its real result even if cancellation arrives.
        return completion.Task;
    }

    [SupportedOSPlatform("windows")]
    private static void Recycle(string path, CancellationToken token)
    {
        var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new("3AD05575-8857-4850-9277-11B85BDB8E09"), true)!)!;
        IShellItem? item = null;
        try
        {
            var iid = typeof(IShellItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref iid, out item));
            // RECYCLEONDELETE forces recycling; EARLYFAILURE prevents ignored errors. No dialogs.
            operation.SetOperationFlags(0x00080000 | 0x00100000 | 0x20000000 | 0x0004 | 0x0010 | 0x0400);
            var sink = new RecycleSink(token);
            operation.DeleteItem(item, sink);
            operation.PerformOperations();
            operation.GetAnyOperationsAborted(out var aborted);
            if (sink.RejectedPermanentDelete) throw new NotSupportedException("Windows did not offer recycling; deletion was refused.");
            if (!sink.Completed) token.ThrowIfCancellationRequested();
            if (aborted || !sink.Completed || sink.Result < 0 || Directory.Exists(path))
                throw new IOException($"Windows did not complete project recycling (HRESULT 0x{sink.Result:X8}).");
        }
        finally
        {
            if (item is not null) Marshal.FinalReleaseComObject(item);
            Marshal.FinalReleaseComObject(operation);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindingContext, ref Guid iid, out IShellItem item);

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class RecycleSink(CancellationToken token) : IFileOperationProgressSink
    {
        public bool RejectedPermanentDelete { get; private set; }
        public bool Completed { get; private set; }
        public int Result { get; private set; }
        public int StartOperations() => 0;
        public int FinishOperations(int result) { if (result < 0) Result = result; return 0; }
        public int PreDeleteItem(uint flags, IShellItem item)
        {
            if ((flags & 0x80) == 0) { RejectedPermanentDelete = true; return unchecked((int)0x80004005); }
            return token.IsCancellationRequested ? unchecked((int)0x800704C7) : 0;
        }
        public int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? created) { Result = result; Completed = result >= 0; return 0; }
        public int PreRenameItem(uint flags, IShellItem item, string name) => 0;
        public int PostRenameItem(uint flags, IShellItem item, string name, int result, IShellItem? created) => 0;
        public int PreMoveItem(uint flags, IShellItem item, IShellItem destination, string name) => 0;
        public int PostMoveItem(uint flags, IShellItem item, IShellItem destination, string name, int result, IShellItem? created) => 0;
        public int PreCopyItem(uint flags, IShellItem item, IShellItem destination, string name) => 0;
        public int PostCopyItem(uint flags, IShellItem item, IShellItem destination, string name, int result, IShellItem? created) => 0;
        public int PreNewItem(uint flags, IShellItem destination, string name) => 0;
        public int PostNewItem(uint flags, IShellItem destination, string name, string templateName, uint attributes, int result, IShellItem? created) => 0;
        public int UpdateProgress(uint total, uint current) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        void BindToHandler(nint context, ref Guid handler, ref Guid iid, out nint result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint displayName, out nint name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IFileOperationProgressSink sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog([MarshalAs(UnmanagedType.Interface)] object dialog);
        void SetProperties([MarshalAs(UnmanagedType.Interface)] object properties);
        void SetOwnerWindow(nint owner);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
        void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
        void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
        void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
        void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
        void DeleteItem(IShellItem item, IFileOperationProgressSink sink);
        void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string templateName, IFileOperationProgressSink sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
        [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
        [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
        [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
        [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? created);
        [PreserveSig] int PreNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string templateName, uint attributes, int result, IShellItem? created);
        [PreserveSig] int UpdateProgress(uint total, uint current);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }
}
