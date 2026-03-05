namespace AngularAppAnalyser.Abstractions;

/// <summary>
/// Contract for a single analysis step. Implement this interface to add a new
/// analysis area — the engine discovers all implementations automatically.
/// </summary>
public interface IAnalysisStep
{
    /// <summary>Display name shown in console output (e.g. "Security vulnerabilities").</summary>
    string Name { get; }

    /// <summary>Emoji prefix for console output.</summary>
    string Icon { get; }

    /// <summary>Execution order. Lower values run first.</summary>
    int Order { get; }

    /// <summary>Run the analysis, writing results into <paramref name="context"/>.</summary>
    void Execute(AnalysisContext context);

    /// <summary>Return a short summary line printed after this step completes.</summary>
    string GetSummary(AnalysisContext context);
}
