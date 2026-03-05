using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DotNetCrawler.Models;

namespace DotNetCrawler.Reporting
{
    internal static class ConsoleReportWriter
    {
        public static void DisplayCodeUsageSummary(List<ProjectAnalysisResult> results)
        {
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("?? CODE USAGE ANALYSIS");
            Console.WriteLine(new string('=', 60));

            foreach (var projectResult in results)
            {
                if (!projectResult.PackageUsages.Any())
                    continue;

                Console.WriteLine($"\n?? Project: {projectResult.ProjectName}");

                foreach (var packageUsage in projectResult.PackageUsages)
                {
                    Console.WriteLine($"\n  ?? {packageUsage.PackageName} (v{packageUsage.Version})");
                    Console.WriteLine($"     Namespaces: {packageUsage.UsedNamespaces.Count}");
                    Console.WriteLine($"     Types: {packageUsage.UsedTypes.Count}");
                    Console.WriteLine($"     Files: {packageUsage.Files.Count}");

                    if (packageUsage.UsedNamespaces.Any())
                    {
                        Console.WriteLine($"     Namespaces with Types:");
                        foreach (var ns in packageUsage.UsedNamespaces.OrderBy(n => n))
                        {
                            Console.WriteLine($"        ?? {ns}");

                            if (packageUsage.TypesByNamespace.ContainsKey(ns) && packageUsage.TypesByNamespace[ns].Any())
                            {
                                var types = packageUsage.TypesByNamespace[ns].OrderBy(t => t).Take(5).ToList();
                                foreach (var type in types)
                                    Console.WriteLine($"           - {type}");
                                if (packageUsage.TypesByNamespace[ns].Count > 5)
                                    Console.WriteLine($"           ... and {packageUsage.TypesByNamespace[ns].Count - 5} more");
                            }
                            else
                            {
                                Console.WriteLine($"           (namespace imported, no specific types detected)");
                            }
                        }
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"     ??  Package referenced but not used in code!");
                        Console.ResetColor();
                    }
                }
            }
        }

        public static void DisplayManifestSummary(List<DllManifestAnalysis> manifestResults)
        {
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("?? DLL MANIFEST DEPENDENCY ANALYSIS");
            Console.WriteLine(new string('=', 60));

            var analyzedCount = manifestResults.Count(r => r.Error == null);
            var blockerPackages = manifestResults.Where(r => r.Dependencies.Any(d => d.IsBlocker)).ToList();
            var cleanPackages = manifestResults.Where(r => r.Error == null && !r.Dependencies.Any(d => d.IsBlocker)).ToList();

            Console.WriteLine($"\n  Packages analyzed: {analyzedCount}");
            Console.WriteLine($"  With blockers:     {blockerPackages.Count}");
            Console.WriteLine($"  Clean:             {cleanPackages.Count}");

            if (blockerPackages.Any())
            {
                Console.WriteLine("\n  ? PACKAGES WITH .NET FRAMEWORK-ONLY DEPENDENCIES:");
                Console.WriteLine(new string('-', 50));

                foreach (var pkg in blockerPackages)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n  ?? {pkg.PackageName}");
                    Console.ResetColor();

                    if (!string.IsNullOrEmpty(pkg.TargetFramework))
                        Console.WriteLine($"     Target Framework: {pkg.TargetFramework}");

                    Console.WriteLine($"     DLL: {Path.GetFileName(pkg.DllPath)}");
                    Console.WriteLine($"     Blocker dependencies:");

                    foreach (var dep in pkg.Dependencies.Where(d => d.IsBlocker))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"       ??  {dep.AssemblyName} v{dep.Version}");
                        Console.ResetColor();
                        Console.WriteLine($"          Reason: {dep.BlockerReason}");
                        Console.WriteLine($"          Category: {dep.BlockerCategory}");
                    }
                }
            }

            if (cleanPackages.Any())
            {
                Console.WriteLine("\n  ? PACKAGES WITH NO DETECTED BLOCKERS:");
                Console.WriteLine(new string('-', 50));

                foreach (var pkg in cleanPackages)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write($"  ?? {pkg.PackageName}");
                    Console.ResetColor();

                    if (!string.IsNullOrEmpty(pkg.TargetFramework))
                        Console.Write($"  (Target: {pkg.TargetFramework})");

                    Console.WriteLine($"  [{pkg.Dependencies.Count} deps scanned]");
                }
            }

            Console.WriteLine();
        }
    }
}
