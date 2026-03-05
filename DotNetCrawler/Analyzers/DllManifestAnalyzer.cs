using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DotNetCrawler.Models;

namespace DotNetCrawler.Analyzers
{
    internal static class DllManifestAnalyzer
    {
        public static List<DllManifestAnalysis> Analyze(
            string solutionDir,
            List<ProjectInfo> projects,
            Dictionary<string, HashSet<string>> matchedPackages,
            string[] binPaths)
        {
            var results = new List<DllManifestAnalysis>();
            var frameworkOnlyNuGets = FrameworkCompatibility.GetFrameworkOnlyNuGetPackages();
            Console.WriteLine($"   Loaded {frameworkOnlyNuGets.Count} known .NET Framework-only NuGet entries");

            var dllSearchPaths = BuildSearchPaths(solutionDir, projects, binPaths);

            var distinctDllPaths = dllSearchPaths.Distinct().ToList();
            Console.WriteLine($"   {distinctDllPaths.Count} search path(s) available for DLL lookup");
            Console.WriteLine($"   Scanning {matchedPackages.Count} matched package(s)...");
            Console.WriteLine();

            ResolveEventHandler resolveHandler = (sender, args) =>
            {
                try
                {
                    return System.Reflection.Assembly.ReflectionOnlyLoad(args.Name);
                }
                catch
                {
                    var asmName = new AssemblyName(args.Name);
                    foreach (var searchPath in dllSearchPaths)
                    {
                        var candidatePath = Path.Combine(searchPath, asmName.Name + ".dll");
                        if (File.Exists(candidatePath))
                        {
                            try { return System.Reflection.Assembly.ReflectionOnlyLoadFrom(candidatePath); }
                            catch { }
                        }
                    }
                    return null;
                }
            };

            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += resolveHandler;

            try
            {
                var pkgIndex = 0;
                foreach (var packageName in matchedPackages.Keys)
                {
                    pkgIndex++;
                    Console.Write($"   [{pkgIndex}/{matchedPackages.Count}] {packageName}: ");
                    var dllPath = FindPackageDll(dllSearchPaths, packageName);

                    var analysis = new DllManifestAnalysis
                    {
                        PackageName = packageName,
                        DllPath = dllPath
                    };

                    if (dllPath == null)
                    {
                        analysis.Error = "DLL not found in search paths";
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("DLL not found in search paths");
                        Console.ResetColor();
                        results.Add(analysis);
                        continue;
                    }

                    Console.WriteLine($"found at {Path.GetFileName(dllPath)}");

                    try
                    {
                        var assembly = System.Reflection.Assembly.ReflectionOnlyLoadFrom(dllPath);
                        analysis.AssemblyFullName = assembly.FullName;

                        try
                        {
                            var customAttrs = CustomAttributeData.GetCustomAttributes(assembly);
                            var tfAttr = customAttrs
                                .FirstOrDefault(a => a.AttributeType.Name == "TargetFrameworkAttribute");
                            if (tfAttr != null && tfAttr.ConstructorArguments.Count > 0)
                                analysis.TargetFramework = tfAttr.ConstructorArguments[0].Value?.ToString();
                        }
                        catch { }

                        var referencedAssemblies = assembly.GetReferencedAssemblies();
                        foreach (var refAsm in referencedAssemblies)
                        {
                            var dep = new ManifestDependencyInfo
                            {
                                AssemblyName = refAsm.Name,
                                Version = refAsm.Version?.ToString() ?? "unknown"
                            };

                            var assemblyBlocker = FrameworkCompatibility.IsFrameworkOnlyAssembly(refAsm.Name);
                            if (assemblyBlocker != null)
                            {
                                dep.IsBlocker = true;
                                dep.BlockerReason = assemblyBlocker;
                                dep.BlockerCategory = "Framework Assembly";
                            }

                            if (!dep.IsBlocker && frameworkOnlyNuGets.ContainsKey(refAsm.Name))
                            {
                                dep.IsBlocker = true;
                                dep.BlockerReason = frameworkOnlyNuGets[refAsm.Name];
                                dep.BlockerCategory = "Framework-Only NuGet";
                            }

                            analysis.Dependencies.Add(dep);
                        }

                        var blockerCount = analysis.Dependencies.Count(d => d.IsBlocker);
                        if (!string.IsNullOrEmpty(analysis.TargetFramework))
                            Console.WriteLine($"              Target: {analysis.TargetFramework}");
                        Console.WriteLine($"              {analysis.Dependencies.Count} referenced assembly(ies) in manifest");
                        if (blockerCount > 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"              ? {blockerCount} BLOCKER(S) DETECTED:");
                            Console.ResetColor();
                            foreach (var blocker in analysis.Dependencies.Where(d => d.IsBlocker))
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"                 ? {blocker.AssemblyName} v{blocker.Version} ({blocker.BlockerCategory})");
                                Console.ResetColor();
                            }
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"              ? No blockers detected");
                            Console.ResetColor();
                        }
                    }
                    catch (Exception ex)
                    {
                        analysis.Error = ex.Message;
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"              ??  Could not read manifest: {ex.Message}");
                        Console.ResetColor();
                    }

                    results.Add(analysis);
                }
            }
            finally
            {
                AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= resolveHandler;
            }

            return results;
        }

        private static List<string> BuildSearchPaths(string solutionDir, List<ProjectInfo> projects, string[] binPaths)
        {
            var dllSearchPaths = new List<string>();

            if (binPaths != null && binPaths.Any())
            {
                Console.WriteLine($"   Using {binPaths.Length} custom bin path(s) for DLL discovery");
                dllSearchPaths.AddRange(binPaths);
            }
            else
            {
                Console.WriteLine("   Auto-detecting DLL search paths...");
                foreach (var project in projects)
                {
                    var projectDir = Path.GetDirectoryName(project.Path);
                    var binDebug = Path.Combine(projectDir, "bin", "Debug");
                    var binRelease = Path.Combine(projectDir, "bin", "Release");

                    if (Directory.Exists(binRelease))
                    {
                        foreach (var dir in Directory.GetDirectories(binRelease, "*", SearchOption.AllDirectories))
                            dllSearchPaths.Add(dir);
                        dllSearchPaths.Add(binRelease);
                    }
                    if (Directory.Exists(binDebug))
                    {
                        foreach (var dir in Directory.GetDirectories(binDebug, "*", SearchOption.AllDirectories))
                            dllSearchPaths.Add(dir);
                        dllSearchPaths.Add(binDebug);
                    }
                }

                var packagesFolder = Path.Combine(solutionDir, "packages");
                if (Directory.Exists(packagesFolder))
                {
                    var packageDllFolders = Directory.GetDirectories(packagesFolder, "*", SearchOption.AllDirectories)
                        .Where(d => d.Contains("\\lib\\"));
                    dllSearchPaths.AddRange(packageDllFolders);
                }
            }

            return dllSearchPaths;
        }

        private static string FindPackageDll(List<string> searchPaths, string packageName)
        {
            foreach (var searchPath in searchPaths.Distinct())
            {
                if (!Directory.Exists(searchPath)) continue;

                var exactPath = Path.Combine(searchPath, packageName + ".dll");
                if (File.Exists(exactPath)) return exactPath;
            }

            foreach (var searchPath in searchPaths.Distinct())
            {
                if (!Directory.Exists(searchPath)) continue;

                if (searchPath.IndexOf(packageName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var dlls = Directory.GetFiles(searchPath, "*.dll", SearchOption.TopDirectoryOnly);
                    var match = dlls.FirstOrDefault(d =>
                        Path.GetFileNameWithoutExtension(d).Equals(packageName, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match;
                }
            }

            return null;
        }
    }
}
