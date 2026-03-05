namespace AngularAppAnalyser.Abstractions;

/// <summary>
/// Contract for report output. Implement this to add new output formats
/// (HTML, JSON, Markdown, etc.).
/// </summary>
public interface IReportWriter
{
    /// <summary>Write the completed report.</summary>
    void Write(Models.AnalysisReport report, string? outputPath);
}
