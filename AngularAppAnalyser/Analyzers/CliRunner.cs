using System.Diagnostics;
using System.Text;

namespace AngularAppAnalyser.Analyzers;

/// <summary>
/// Shared helper for running CLI tools and capturing output.
/// </summary>
internal static class CliRunner
{
    /// <param name="progressLabel">
    /// When set, shows an elapsed-time spinner on the console while the process runs.
    /// </param>
    public static (string stdout, string stderr, int exitCode) Run(
        string workingDir, string fileName, string arguments,
        int timeoutMs = 60_000, string? progressLabel = null)
    {
        var isWindows = OperatingSystem.IsWindows();

        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : fileName,
            Arguments = isWindows ? $"/c {fileName} {arguments}" : arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return ("", $"{fileName} not found", -1);

            // Read streams asynchronously to avoid deadlocks
            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdoutBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderrBuilder.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (progressLabel is not null)
            {
                var sw = Stopwatch.StartNew();
                while (!process.WaitForExit(2_000))
                {
                    Console.Write($"\r   \u23f3 {progressLabel} ({sw.Elapsed.TotalSeconds:F0}s)...   ");
                    if (sw.ElapsedMilliseconds > timeoutMs)
                    {
                        process.Kill(entireProcessTree: true);
                        Console.WriteLine($"\r   \u26a0\ufe0f {progressLabel} — timed out after {timeoutMs / 1000}s          ");
                        return (stdoutBuilder.ToString(), "Process timed out", -2);
                    }
                }
                // Clear the progress line
                Console.Write("\r                                                                        \r");
            }
            else
            {
                process.WaitForExit(timeoutMs);
            }

            // Ensure async reads are flushed
            process.WaitForExit();

            return (stdoutBuilder.ToString(), stderrBuilder.ToString(), process.ExitCode);
        }
        catch (Exception ex)
        {
            return ("", ex.Message, -1);
        }
    }
}
