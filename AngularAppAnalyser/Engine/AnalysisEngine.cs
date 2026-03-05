using System.Diagnostics;
using AngularAppAnalyser.Abstractions;

namespace AngularAppAnalyser.Engine;

/// <summary>
/// Orchestrates all <see cref="IAnalysisStep"/> implementations and
/// delegates report output to <see cref="IReportWriter"/> instances.
/// </summary>
public sealed class AnalysisEngine
{
    private readonly List<IAnalysisStep> _steps;
    private readonly List<IReportWriter> _writers;

    public AnalysisEngine(IEnumerable<IAnalysisStep> steps, IEnumerable<IReportWriter> writers)
    {
        _steps = steps.OrderBy(s => s.Order).ToList();
        _writers = writers.ToList();
    }

    public void Run(string appPath, string? outputFile)
    {
        var stopwatch = Stopwatch.StartNew();

        Console.WriteLine(new string('=', 60));
        Console.WriteLine("\ud83d\udd0d Angular App Analyser");
        Console.WriteLine($"   Path: {appPath}");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine();

        if (!Directory.Exists(appPath))
        {
            Console.Error.WriteLine($"Error: Directory not found: {appPath}");
            return;
        }

        if (!File.Exists(Path.Combine(appPath, "package.json")))
        {
            Console.Error.WriteLine($"Error: package.json not found in {appPath}. Is this an Angular project?");
            return;
        }

        var context = new AnalysisContext(appPath);
        var totalSteps = _steps.Count;

        for (var i = 0; i < totalSteps; i++)
        {
            var step = _steps[i];
            var stepNum = i + 1;
            Console.WriteLine($"[Step {stepNum}/{totalSteps}] {step.Icon} {step.Name}...");

            var stepStart = stopwatch.ElapsedMilliseconds;
            step.Execute(context);
            var elapsed = stopwatch.ElapsedMilliseconds - stepStart;

            Console.WriteLine($"[Step {stepNum}/{totalSteps}] \u2705 {step.GetSummary(context)} ({elapsed}ms)");
            Console.WriteLine();
        }

        // Write reports
        foreach (var writer in _writers)
            writer.Write(context.Report, outputFile);

        Console.WriteLine($"\n\u23f1\ufe0f  Total elapsed time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine("\u2728 Analysis complete!");
    }
}
