namespace HostPlatform.Transport;

/// <summary>
/// Finds the minimodem program. The live call uses <see cref="Bell202Link"/>
/// because minimodem reads and writes whole audio files. This type records
/// that the same Bell 202 mode is what <c>minimodem 1200</c> implements, and
/// it can shell out to that program for a file check.
/// </summary>
public static class MinimodemTool
{
    public static string? Find()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;
        var name = OperatingSystem.IsWindows() ? "minimodem.exe" : "minimodem";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), name);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Decodes a 16-bit PCM WAV with <c>minimodem --rx 1200</c>. Returns null
    /// when the program is not installed.
    /// </summary>
    public static byte[]? DecodeWav(string wavPath)
    {
        var bin = Find();
        if (bin == null || !File.Exists(wavPath))
            return null;
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = bin,
            ArgumentList = { "--rx", "--quiet", "--samplerate", "8000", "--file", wavPath, "1200" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = System.Diagnostics.Process.Start(start);
        if (process == null)
            return null;
        using var stdout = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(stdout);
        process.WaitForExit(20_000);
        return process.ExitCode == 0 ? stdout.ToArray() : null;
    }
}
