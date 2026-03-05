using AngularAppAnalyser.Abstractions;
using AngularAppAnalyser.Analyzers;
using AngularAppAnalyser.Engine;
using AngularAppAnalyser.Reporting;

namespace AngularAppAnalyser;

internal class Program
{
    static int Main(string[] args)
    {
        try
        {
            var parsedArgs = ParseArguments(args);
            if (parsedArgs is null)
            {
                ShowUsage();
                return 1;
            }

            var appPath = Path.GetFullPath(parsedArgs.AppPath);

            // Register all analysis steps (add new ones here)
            IAnalysisStep[] steps =
            [
                new ProjectMetadataStep(),
                new SecurityStep(),
                new StorageStep(),
                new ApiCommunicationStep(),
                new DesignStep(),
                new AppStructureStep(),
                new LibraryHealthStep(),
                new LibraryVersionStep(),
                new SecurityPostureStep(),
                new NpmAuditStep(),
                new LinterStep(),
                new TypeCheckStep(),
            ];

            // Register report writers
            IReportWriter[] writers =
            [
                new ConsoleReportWriter(),
                new HtmlReportWriter(),
                new JsonReportWriter(),
            ];

            var engine = new AnalysisEngine(steps, writers);
            engine.Run(appPath, parsedArgs.OutputFile);
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static ParsedArguments? ParseArguments(string[] args)
    {
        string? appPath = null;
        string? outputFile = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--path" or "-p":
                    if (i + 1 < args.Length) appPath = args[++i];
                    break;
                case "--output" or "-o":
                    if (i + 1 < args.Length) outputFile = args[++i];
                    break;
            }
        }

        if (string.IsNullOrEmpty(appPath)) return null;
        return new ParsedArguments { AppPath = appPath, OutputFile = outputFile };
    }

    static void ShowUsage()
    {
        Console.WriteLine("Angular App Analyser \u2014 Security & Health Scanner");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  AngularAppAnalyser --path <angular-app-folder> [--output <report.html>]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --path <path>       Path to the Angular application root (containing package.json)");
        Console.WriteLine("  -o, --output <file>     Path to output HTML report (optional)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  AngularAppAnalyser -p ./my-angular-app");
        Console.WriteLine("  AngularAppAnalyser --path C:\\Projects\\MyApp --output report.html");
    }

    private class ParsedArguments
    {
        public required string AppPath { get; set; }
        public string? OutputFile { get; set; }
    }
}
