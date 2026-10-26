using System.Diagnostics;

namespace GalNet.TestSupport;

internal static class DirectoryLinks
{
    // These tests reject directory reparse points, including links in ancestor paths.
    public static void Create(string path, string target)
    {
        try { Directory.CreateSymbolicLink(path, target); }
        catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 1314)
        {
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardError = true
            };
            static string Quote(string value) => "'" + Path.GetFullPath(value).Replace("'", "''", StringComparison.Ordinal) + "'";
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command",
                "$null = New-Item -ItemType Junction -Path " + Quote(path) + " -Target " + Quote(target) + " -ErrorAction Stop" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var diagnostic = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new IOException("Could not create test directory junction: " + diagnostic, error);
            TestContext.Out.WriteLine("Symlink privilege unavailable; exercising the directory reparse-point boundary with a junction.");
        }
    }
}
