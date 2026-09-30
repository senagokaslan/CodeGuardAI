using System.Diagnostics;

namespace CodeGuardAI.UnitTests.RepositorySecurity;

internal static class ReparsePointFixture
{
    public static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/d /c mklink /J \"{linkPath}\" \"{targetPath}\"",
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Junction fixture process could not be started.");

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Junction fixture could not be created.");
        }
    }

    public static void DeleteDirectoryLink(string linkPath)
    {
        if (Directory.Exists(linkPath))
        {
            Directory.Delete(linkPath);
        }
    }
}
