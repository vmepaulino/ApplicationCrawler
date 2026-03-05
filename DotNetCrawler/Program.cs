using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotNetCrawler.Analyzers;
using DotNetCrawler.Models;
using DotNetCrawler.Reporting;

namespace DotNetCrawler
{
    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var parsedArgs = ParseArguments(args);
                if (parsedArgs == null)
                {
                    ShowUsage();
                    return;
                }

                RunDependencyAnalysis(parsedArgs).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
                Console.WriteLine(ex.StackTrace);
            }
        }

        // ?? Dependency analysis (--dependencies mode) ???????????

        static async Task RunDependencyAnalysis(ParsedArguments parsedArgs)
        {
            var solutionPath = parsedArgs.SolutionPath;
            if (!File.Exists(solutionPath))
            {
                Console.Error.WriteLine($"Error: Solution file not found: {solutionPath}");
                return;
            }

            var stopwatch = Stopwatch.StartNew();

            Console.WriteLine(new string('=', 60));
            Console.WriteLine($"?? Analyzing solution: {Path.GetFileName(solutionPath)}");
            Console.WriteLine($"   Path: {Path.GetFullPath(solutionPath)}");
            Console.WriteLine($"?? Dependency patterns: {string.Join(", ", parsedArgs.DependencyPatterns)}");
            if (parsedArgs.BinPaths != null && parsedArgs.BinPaths.Length > 0)
                Console.WriteLine($"?? Custom bin paths: {string.Join(", ", parsedArgs.BinPaths)}");
            Console.WriteLine(new string('=', 60));
            Console.WriteLine();

            // Step 1: Parse solution
            Console.WriteLine("[Step 1/5] ? Parsing solution file...");
            var solutionDir = Path.GetDirectoryName(solutionPath);
            var projectPaths = SolutionParser.ParseSolutionFile(solutionPath);
            Console.WriteLine($"[Step 1/5] ? Found {projectPaths.Count} project(s) in solution ({stopwatch.ElapsedMilliseconds}ms)");
            Console.WriteLine();

            // Step 2: Scan for matching NuGet packages
            Console.WriteLine("[Step 2/5] ?? Scanning projects for matching NuGet packages...");
            Console.WriteLine();

            var patterns = parsedArgs.DependencyPatterns.Select(ConvertWildcardToRegex).ToArray();
            var matchedPackages = new Dictionary<string, HashSet<string>>();
            var projectInfos = new List<ProjectInfo>();
            var projectIndex = 0;

            foreach (var projectPath in projectPaths)
            {
                projectIndex++;
                var fullProjectPath = Path.Combine(solutionDir, projectPath);
                if (!File.Exists(fullProjectPath))
                {
                    Console.WriteLine($"??  Project file not found: {projectPath}");
                    continue;
                }

                var projectName = Path.GetFileNameWithoutExtension(projectPath);
                Console.WriteLine($"   [{projectIndex}/{projectPaths.Count}] ?? {projectName}");

                var packages = SolutionParser.GetNuGetPackagesFromProjectFile(fullProjectPath);
                Console.WriteLine($"          {packages.Count} package reference(s) found");

                foreach (var package in packages)
                {
                    if (patterns.Any(p => p.IsMatch(package.Name)))
                    {
                        Console.WriteLine($"   ? Found: {package.Name} (v{package.Version})");
                        if (!matchedPackages.ContainsKey(package.Name))
                            matchedPackages[package.Name] = new HashSet<string>();
                        matchedPackages[package.Name].Add(projectName);
                    }
                }

                if (!packages.Any(p => patterns.Any(pat => pat.IsMatch(p.Name))))
                    Console.WriteLine($"   - No matching packages");

                projectInfos.Add(new ProjectInfo
                {
                    Name = projectName,
                    Path = fullProjectPath,
                    Packages = packages
                });

                Console.WriteLine();
            }

            Console.WriteLine();
            Console.WriteLine(new string('=', 60));
            Console.WriteLine("?? PACKAGE SCAN SUMMARY");
            Console.WriteLine(new string('=', 60));
            Console.WriteLine($"   Scanned {projectPaths.Count} project(s) in {stopwatch.ElapsedMilliseconds}ms");

            if (matchedPackages.Any())
            {
                Console.WriteLine($"   Found {matchedPackages.Count} matching package(s):");
                foreach (var kvp in matchedPackages.OrderBy(x => x.Key))
                    Console.WriteLine($"     ?? {kvp.Key} ? {kvp.Value.Count} project(s): {string.Join(", ", kvp.Value)}");
                Console.WriteLine();

                // Step 3: Load DLL references
                Console.WriteLine($"[Step 3/5] ?? Loading assembly references for semantic analysis...");
                var stepStart = stopwatch.ElapsedMilliseconds;
                var metadataReferences = CodeUsageAnalyzer.LoadMetadataReferences(solutionDir, projectInfos, parsedArgs.BinPaths);
                Console.WriteLine($"[Step 3/5] ? Assembly references loaded ({stopwatch.ElapsedMilliseconds - stepStart}ms)");
                Console.WriteLine();

                // Step 4: Analyze code usage
                Console.WriteLine($"[Step 4/5] ?? Analyzing code usage with Roslyn semantic analysis...");
                stepStart = stopwatch.ElapsedMilliseconds;
                var analysisResults = await CodeUsageAnalyzer.AnalyzeCodeUsageByProjectAsync(
                    projectInfos, matchedPackages.Keys.ToArray(), metadataReferences);
                Console.WriteLine($"[Step 4/5] ? Code usage analysis complete ({stopwatch.ElapsedMilliseconds - stepStart}ms)");
                Console.WriteLine();

                // Step 5: Analyze DLL manifests
                Console.WriteLine($"[Step 5/5] ?? Analyzing DLL assembly manifests for migration blockers...");
                stepStart = stopwatch.ElapsedMilliseconds;
                List<DllManifestAnalysis> manifestResults;
                try
                {
                    manifestResults = DllManifestAnalyzer.Analyze(solutionDir, projectInfos, matchedPackages, parsedArgs.BinPaths);
                    Console.WriteLine($"[Step 5/5] ? Manifest analysis complete ({stopwatch.ElapsedMilliseconds - stepStart}ms)");
                }
                catch (Exception ex)
                {
                    manifestResults = new List<DllManifestAnalysis>();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[Step 5/5] ??  Manifest analysis failed: {ex.Message}");
                    Console.ResetColor();
                }

                // Display console summary
                ConsoleReportWriter.DisplayCodeUsageSummary(analysisResults);
                ConsoleReportWriter.DisplayManifestSummary(manifestResults);

                // Generate HTML report
                if (!string.IsNullOrEmpty(parsedArgs.OutputFile))
                {
                    Console.WriteLine("\n?? Generating HTML report...");
                    HtmlReportWriter.Generate(solutionPath, analysisResults, manifestResults, parsedArgs.OutputFile);
                    Console.WriteLine($"?? HTML report generated: {Path.GetFullPath(parsedArgs.OutputFile)}");
                }

                Console.WriteLine($"\n??  Total elapsed time: {stopwatch.ElapsedMilliseconds}ms");
            }
            else
            {
                Console.WriteLine("\n??  No matching packages found.");
            }

            Console.WriteLine("\n? Analysis complete! Press any key to exit...");
            Console.ReadKey();
        }

        // ?? Argument parsing ????????????????????????????????????

        static ParsedArguments ParseArguments(string[] args)
        {
            string solutionPath = null;
            string outputFile = null;
            var dependencies = new List<string>();
            var binPaths = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--solution" || args[i] == "-s")
                {
                    if (i + 1 < args.Length)
                    {
                        solutionPath = args[i + 1];
                        i++;
                    }
                }
                else if (args[i] == "--output" || args[i] == "-o")
                {
                    if (i + 1 < args.Length)
                    {
                        outputFile = args[i + 1];
                        i++;
                    }
                }
                else if (args[i] == "--bin-paths" || args[i] == "-b")
                {
                    for (int j = i + 1; j < args.Length && !args[j].StartsWith("-"); j++)
                    {
                        binPaths.Add(args[j]);
                        i = j;
                    }
                }
                else if (args[i] == "--dependencies" || args[i] == "-d")
                {
                    for (int j = i + 1; j < args.Length && !args[j].StartsWith("-"); j++)
                    {
                        dependencies.Add(args[j]);
                        i = j;
                    }
                }
            }

            if (string.IsNullOrEmpty(solutionPath) || !dependencies.Any())
                return null;

            return new ParsedArguments
            {
                SolutionPath = solutionPath,
                DependencyPatterns = dependencies.ToArray(),
                OutputFile = outputFile,
                BinPaths = binPaths.Any() ? binPaths.ToArray() : null
            };
        }

        static void ShowUsage()
        {
            Console.WriteLine("Application Crawler - Analyzes .NET solutions");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  DotNetCrawler.exe --solution <path> --dependencies <pattern1> [pattern2] ... [--output <file>] [--bin-paths <path1> ...]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -s, --solution <path>          Path to the solution file (.sln)");
            Console.WriteLine("  -d, --dependencies <patterns>  NuGet package name patterns (supports wildcards)");
            Console.WriteLine("  -o, --output <file>            Path to output HTML report (optional)");
            Console.WriteLine("  -b, --bin-paths <paths>        Paths to bin folders with compiled DLLs (optional)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  DotNetCrawler.exe -s MySolution.sln -d \"Newtonsoft.*\" \"System.Net.Http\"");
            Console.WriteLine("  DotNetCrawler.exe -s App.sln -d \"MyLib.*\" --output report.html");
            Console.WriteLine("  DotNetCrawler.exe -s App.sln -d \"MyLib.*\" -b \"bin\\Debug\" \"bin\\Release\"");
        }

        // ?? Helpers ?????????????????????????????????????????????

        static Regex ConvertWildcardToRegex(string pattern)
        {
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return new Regex(regexPattern, RegexOptions.IgnoreCase);
        }
    }
}
