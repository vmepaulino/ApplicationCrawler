using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

                MainAsync(parsedArgs.SolutionPath, parsedArgs.DependencyPatterns, parsedArgs.OutputFile, parsedArgs.BinPaths).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
                Console.WriteLine(ex.StackTrace);
            }
        }

        static async Task MainAsync(string solutionPath, string[] dependencyPatterns, string outputFile = null, string[] binPaths = null)
        {
            if (!File.Exists(solutionPath))
            {
                Console.Error.WriteLine($"Error: Solution file not found: {solutionPath}");
                return;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            Console.WriteLine(new string('=', 60));
            Console.WriteLine($"🔍 Analyzing solution: {Path.GetFileName(solutionPath)}");
            Console.WriteLine($"   Path: {Path.GetFullPath(solutionPath)}");
            Console.WriteLine($"📦 Dependency patterns: {string.Join(", ", dependencyPatterns)}");
            if (binPaths != null && binPaths.Length > 0)
                Console.WriteLine($"📂 Custom bin paths: {string.Join(", ", binPaths)}");
            Console.WriteLine(new string('=', 60));
            Console.WriteLine();

            Console.WriteLine("[Step 1/5] ⏳ Parsing solution file...");
            
            var solutionDir = Path.GetDirectoryName(solutionPath);
            var projectPaths = ParseSolutionFile(solutionPath);
            
            Console.WriteLine($"[Step 1/5] ✅ Found {projectPaths.Count} project(s) in solution ({stopwatch.ElapsedMilliseconds}ms)");
            Console.WriteLine();

            Console.WriteLine("[Step 2/5] 📦 Scanning projects for matching NuGet packages...");
            Console.WriteLine();

            var patterns = dependencyPatterns.Select(ConvertWildcardToRegex).ToArray();
            var matchedPackages = new Dictionary<string, HashSet<string>>(); // Package -> Projects
            var projectInfos = new List<ProjectInfo>();
            var projectIndex = 0;

            foreach (var projectPath in projectPaths)
            {
                projectIndex++;
                var fullProjectPath = Path.Combine(solutionDir, projectPath);
                if (!File.Exists(fullProjectPath))
                {
                    Console.WriteLine($"⚠️  Project file not found: {projectPath}");
                    continue;
                }

                var projectName = Path.GetFileNameWithoutExtension(projectPath);
                Console.WriteLine($"   [{projectIndex}/{projectPaths.Count}] 📁 {projectName}");

                var packages = GetNuGetPackagesFromProjectFile(fullProjectPath);
                Console.WriteLine($"          {packages.Count} package reference(s) found");

                foreach (var package in packages)
                {
                    if (patterns.Any(p => p.IsMatch(package.Item1)))
                    {
                        Console.WriteLine($"   ✓ Found: {package.Item1} (v{package.Item2})");

                        if (!matchedPackages.ContainsKey(package.Item1))
                        {
                            matchedPackages[package.Item1] = new HashSet<string>();
                        }
                        matchedPackages[package.Item1].Add(projectName);
                    }
                }

                if (!packages.Any(p => patterns.Any(pat => pat.IsMatch(p.Item1))))
                {
                    Console.WriteLine($"   - No matching packages");
                }

                // Collect project info for code analysis
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
            Console.WriteLine("📊 PACKAGE SCAN SUMMARY");
            Console.WriteLine(new string('=', 60));
            Console.WriteLine($"   Scanned {projectPaths.Count} project(s) in {stopwatch.ElapsedMilliseconds}ms");

            if (matchedPackages.Any())
            {
                Console.WriteLine($"   Found {matchedPackages.Count} matching package(s):");
                foreach (var kvp in matchedPackages.OrderBy(x => x.Key))
                {
                    Console.WriteLine($"     📦 {kvp.Key} → {kvp.Value.Count} project(s): {string.Join(", ", kvp.Value)}");
                }
                Console.WriteLine();

                // Step 3: Load DLL references
                Console.WriteLine($"[Step 3/5] 🔧 Loading assembly references for semantic analysis...");
                var stepStart = stopwatch.ElapsedMilliseconds;
                var metadataReferences = LoadMetadataReferences(solutionDir, projectInfos, binPaths);
                Console.WriteLine($"[Step 3/5] ✅ Assembly references loaded ({stopwatch.ElapsedMilliseconds - stepStart}ms)");
                Console.WriteLine();

                // Step 4: Analyze code usage
                Console.WriteLine($"[Step 4/5] 🔬 Analyzing code usage with Roslyn semantic analysis...");
                stepStart = stopwatch.ElapsedMilliseconds;
                var analysisResults = await AnalyzeCodeUsageByProjectAsync(projectInfos, matchedPackages.Keys.ToArray(), metadataReferences);
                Console.WriteLine($"[Step 4/5] ✅ Code usage analysis complete ({stopwatch.ElapsedMilliseconds - stepStart}ms)");
                Console.WriteLine();

                // Step 5: Analyze DLL manifests
                Console.WriteLine($"[Step 5/5] 🔎 Analyzing DLL assembly manifests for migration blockers...");
                stepStart = stopwatch.ElapsedMilliseconds;
                List<DllManifestAnalysis> manifestResults;
                try
                {
                    manifestResults = AnalyzeDllManifests(solutionDir, projectInfos, matchedPackages, binPaths);
                    Console.WriteLine($"[Step 5/5] ✅ Manifest analysis complete ({stopwatch.ElapsedMilliseconds - stepStart}ms)");
                }
                catch (Exception ex)
                {
                    manifestResults = new List<DllManifestAnalysis>();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[Step 5/5] ⚠️  Manifest analysis failed: {ex.Message}");
                    Console.ResetColor();
                }
                
                // Display console summary
                DisplayConsoleSummary(analysisResults);
                DisplayManifestAnalysisSummary(manifestResults);
                
                // Generate HTML report
                if (!string.IsNullOrEmpty(outputFile))
                {
                    Console.WriteLine("\n📝 Generating HTML report...");
                    GenerateHtmlReport(solutionPath, analysisResults, manifestResults, outputFile);
                    Console.WriteLine($"📄 HTML report generated: {Path.GetFullPath(outputFile)}");
                }

                Console.WriteLine($"\n⏱️  Total elapsed time: {stopwatch.ElapsedMilliseconds}ms");
            }
            else
            {
                Console.WriteLine("\n⚠️  No matching packages found.");
            }

            Console.WriteLine("\n✨ Analysis complete! Press any key to exit...");
            Console.ReadKey();
        }

        static List<Tuple<string, string>> GetNuGetPackagesFromProjectFile(string projectFilePath)
        {
            var packages = new List<Tuple<string, string>>();

            try
            {
                // Try SDK-style project (PackageReference)
                var projectXml = XDocument.Load(projectFilePath);
                var packageRefs = projectXml.Descendants()
                    .Where(e => e.Name.LocalName == "PackageReference")
                    .Select(e => new
                    {
                        Name = e.Attribute("Include")?.Value,
                        Version = e.Attribute("Version")?.Value ?? 
                                  e.Elements().FirstOrDefault(el => el.Name.LocalName == "Version")?.Value
                    })
                    .Where(p => !string.IsNullOrEmpty(p.Name));

                packages.AddRange(packageRefs.Select(p => Tuple.Create(p.Name, p.Version ?? "unknown")));

                // Try packages.config for old-style .NET Framework projects
                var projectDir = Path.GetDirectoryName(projectFilePath);
                var packagesConfigPath = Path.Combine(projectDir, "packages.config");

                if (File.Exists(packagesConfigPath))
                {
                    var packagesConfig = XDocument.Load(packagesConfigPath);
                    var packagesFromConfig = packagesConfig.Descendants()
                        .Where(e => e.Name.LocalName == "package")
                        .Select(e => new
                        {
                            Name = e.Attribute("id")?.Value,
                            Version = e.Attribute("version")?.Value
                        })
                        .Where(p => !string.IsNullOrEmpty(p.Name));

                    packages.AddRange(packagesFromConfig.Select(p => Tuple.Create(p.Name, p.Version ?? "unknown")));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️  Warning: Could not read packages: {ex.Message}");
            }

            return packages;
        }

        static List<string> ParseSolutionFile(string solutionPath)
        {
            var projectPaths = new List<string>();
            
            try
            {
                var lines = File.ReadAllLines(solutionPath);
                var projectLineRegex = new Regex(@"Project\(""\{[A-F0-9-]+\}""\)\s*=\s*""[^""]+"",\s*""([^""]+\.csproj)""", RegexOptions.IgnoreCase);

                foreach (var line in lines)
                {
                    var match = projectLineRegex.Match(line);
                    if (match.Success)
                    {
                        projectPaths.Add(match.Groups[1].Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Error parsing solution file: {ex.Message}");
            }

            return projectPaths;
        }

        static Regex ConvertWildcardToRegex(string pattern)
        {
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return new Regex(regexPattern, RegexOptions.IgnoreCase);
        }

        static List<MetadataReference> LoadMetadataReferences(string solutionDir, List<ProjectInfo> projects, string[] binPaths)
        {
            var references = new List<MetadataReference>();
            var loadedDlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add framework references
            var frameworkPath = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            Console.WriteLine($"   Loading .NET Framework base assemblies from: {frameworkPath}");
            var frameworkDlls = new[] { "mscorlib.dll", "System.dll", "System.Core.dll", "System.Xml.dll", "System.Linq.dll" };
            
            foreach (var dll in frameworkDlls)
            {
                var dllPath = Path.Combine(frameworkPath, dll);
                if (File.Exists(dllPath))
                {
                    try
                    {
                        references.Add(MetadataReference.CreateFromFile(dllPath));
                        loadedDlls.Add(dll);
                    }
                    catch { /* Skip if can't load */ }
                }
            }
            Console.WriteLine($"   ✓ {loadedDlls.Count} framework assemblies loaded");

            var searchPaths = new List<string>();

            // Use provided bin paths
            if (binPaths != null && binPaths.Any())
            {
                Console.WriteLine($"   Using {binPaths.Length} custom bin path(s)");
                searchPaths.AddRange(binPaths);
            }
            else
            {
                Console.WriteLine("   Auto-detecting assembly search paths...");
                // Auto-detect bin folders
                foreach (var project in projects)
                {
                    var projectDir = Path.GetDirectoryName(project.Path);
                    var binDebug = Path.Combine(projectDir, "bin", "Debug");
                    var binRelease = Path.Combine(projectDir, "bin", "Release");

                    if (Directory.Exists(binRelease))
                        searchPaths.Add(binRelease);
                    else if (Directory.Exists(binDebug))
                        searchPaths.Add(binDebug);
                }

                // Also check for packages folder
                var packagesFolder = Path.Combine(solutionDir, "packages");
                if (Directory.Exists(packagesFolder))
                {
                    Console.WriteLine($"   Found NuGet packages folder: {packagesFolder}");
                    var packageDllFolders = Directory.GetDirectories(packagesFolder, "*", SearchOption.AllDirectories)
                        .Where(d => d.Contains("\\lib\\") && !d.Contains("\\build\\"));
                    searchPaths.AddRange(packageDllFolders);
                }
                else
                {
                    Console.WriteLine("   ⚠️  No 'packages' folder found (NuGet packages may not be restored)");
                }
            }

            var distinctPaths = searchPaths.Distinct().ToList();
            Console.WriteLine($"   Scanning {distinctPaths.Count} search path(s) for DLLs...");

            // Load DLLs from all search paths
            var failedDlls = 0;
            foreach (var searchPath in distinctPaths)
            {
                if (!Directory.Exists(searchPath))
                    continue;

                var dlls = Directory.GetFiles(searchPath, "*.dll", SearchOption.TopDirectoryOnly);
                
                foreach (var dll in dlls)
                {
                    var dllName = Path.GetFileName(dll);
                    if (loadedDlls.Contains(dllName))
                        continue;

                    try
                    {
                        references.Add(MetadataReference.CreateFromFile(dll));
                        loadedDlls.Add(dllName);
                    }
                    catch
                    {
                        failedDlls++;
                        // Skip DLLs that can't be loaded
                    }
                }
            }

            Console.WriteLine($"   ✅ Loaded {references.Count} assembly references");
            if (failedDlls > 0)
                Console.WriteLine($"   ⚠️  {failedDlls} DLL(s) could not be loaded");
            return references;
        }

        static async Task<List<ProjectAnalysisResult>> AnalyzeCodeUsageByProjectAsync(List<ProjectInfo> projects, string[] packageNames, List<MetadataReference> metadataReferences)
        {
            var results = new List<ProjectAnalysisResult>();
            var projIndex = 0;

            foreach (var projectInfo in projects)
            {
                projIndex++;
                var projectResult = new ProjectAnalysisResult
                {
                    ProjectName = projectInfo.Name,
                    ProjectPath = projectInfo.Path
                };

                var projectDir = Path.GetDirectoryName(projectInfo.Path);
                var csFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
                    .ToList();

                // Get matched packages for this project
                var matchedPackagesForProject = projectInfo.Packages
                    .Where(p => packageNames.Contains(p.Item1, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                Console.WriteLine($"   [{projIndex}/{projects.Count}] 📁 {projectInfo.Name}: {csFiles.Count} .cs file(s), {matchedPackagesForProject.Count} matched package(s)");

                if (!matchedPackagesForProject.Any())
                {
                    results.Add(projectResult);
                    continue;
                }

                foreach (var package in matchedPackagesForProject)
                {
                    var packageUsage = new PackageUsageDetail
                    {
                        PackageName = package.Item1,
                        Version = package.Item2
                    };

                    projectResult.PackageUsages.Add(packageUsage);
                }

                // Create a compilation for this project's files
                Console.WriteLine($"              Parsing {csFiles.Count} source file(s)...");
                var syntaxTrees = new List<SyntaxTree>();
                var fileContents = new Dictionary<string, string>();
                var parseFailures = 0;

                foreach (var csFile in csFiles)
                {
                    try
                    {
                        var code = File.ReadAllText(csFile);
                        fileContents[csFile] = code;
                        var tree = CSharpSyntaxTree.ParseText(code, path: csFile);
                        syntaxTrees.Add(tree);
                    }
                    catch { parseFailures++; }
                }

                if (parseFailures > 0)
                    Console.WriteLine($"              ⚠️  {parseFailures} file(s) could not be parsed");

                // Create compilation with all references
                Console.WriteLine($"              Building Roslyn compilation ({syntaxTrees.Count} trees, {metadataReferences.Count} refs)...");
                var compilation = CSharpCompilation.Create(
                    projectInfo.Name,
                    syntaxTrees: syntaxTrees,
                    references: metadataReferences,
                    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                );

                // Analyze each file with semantic model
                var filesWithUsage = 0;
                var analyzeFailures = 0;
                foreach (var tree in syntaxTrees)
                {
                    try
                    {
                        var semanticModel = compilation.GetSemanticModel(tree);
                        var root = await tree.GetRootAsync();

                        // Look for using directives
                        var usingDirectives = root.DescendantNodes()
                            .OfType<UsingDirectiveSyntax>()
                            .Select(u => u.Name.ToString())
                            .ToHashSet();

                        // Check if any package namespaces are used
                        var fileHasUsage = false;
                        foreach (var packageUsage in projectResult.PackageUsages)
                        {
                            var packageNamespacesInFile = usingDirectives
                                .Where(u => u.StartsWith(packageUsage.PackageName, StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            if (packageNamespacesInFile.Any())
                            {
                                fileHasUsage = true;
                                // Add namespaces
                                foreach (var ns in packageNamespacesInFile)
                                {
                                    packageUsage.UsedNamespaces.Add(ns);
                                    
                                    if (!packageUsage.TypesByNamespace.ContainsKey(ns))
                                    {
                                        packageUsage.TypesByNamespace[ns] = new HashSet<string>();
                                    }
                                }

                                // Extract types using semantic model
                                ExtractTypesWithSemanticModel(root, semanticModel, packageNamespacesInFile, packageUsage);

                                packageUsage.Files.Add(Path.GetFileName(tree.FilePath));
                            }
                        }
                        if (fileHasUsage) filesWithUsage++;
                    }
                    catch (Exception ex)
                    {
                        analyzeFailures++;
                    }
                }

                var totalTypesFound = projectResult.PackageUsages.Sum(p => p.UsedTypes.Count);
                var totalNamespacesFound = projectResult.PackageUsages.Sum(p => p.UsedNamespaces.Count);
                Console.WriteLine($"              ✓ {filesWithUsage} file(s) with usage, {totalNamespacesFound} namespace(s), {totalTypesFound} type(s) detected");
                if (analyzeFailures > 0)
                    Console.WriteLine($"              ⚠️  {analyzeFailures} file(s) could not be analyzed");

                results.Add(projectResult);
            }

            return results;
        }

        static void DisplayConsoleSummary(List<ProjectAnalysisResult> results)
        {
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("📈 CODE USAGE ANALYSIS");
            Console.WriteLine(new string('=', 60));

            foreach (var projectResult in results)
            {
                if (!projectResult.PackageUsages.Any())
                    continue;

                Console.WriteLine($"\n📁 Project: {projectResult.ProjectName}");

                foreach (var packageUsage in projectResult.PackageUsages)
                {
                    Console.WriteLine($"\n  📦 {packageUsage.PackageName} (v{packageUsage.Version})");
                    Console.WriteLine($"     Namespaces: {packageUsage.UsedNamespaces.Count}");
                    Console.WriteLine($"     Types: {packageUsage.UsedTypes.Count}");
                    Console.WriteLine($"     Files: {packageUsage.Files.Count}");

                    if (packageUsage.UsedNamespaces.Any())
                    {
                        Console.WriteLine($"     Namespaces with Types:");
                        foreach (var ns in packageUsage.UsedNamespaces.OrderBy(n => n))
                        {
                            Console.WriteLine($"        📋 {ns}");
                            
                            if (packageUsage.TypesByNamespace.ContainsKey(ns) && packageUsage.TypesByNamespace[ns].Any())
                            {
                                var types = packageUsage.TypesByNamespace[ns].OrderBy(t => t).Take(5).ToList();
                                foreach (var type in types)
                                {
                                    Console.WriteLine($"           - {type}");
                                }
                                if (packageUsage.TypesByNamespace[ns].Count > 5)
                                {
                                    Console.WriteLine($"           ... and {packageUsage.TypesByNamespace[ns].Count - 5} more");
                                }
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
                        Console.WriteLine($"     ⚠️  Package referenced but not used in code!");
                        Console.ResetColor();
                    }
                }
            }
        }

        static void ExtractTypesWithSemanticModel(SyntaxNode root, SemanticModel semanticModel, List<string> packageNamespaces, PackageUsageDetail packageUsage)
        {
            // Get all identifier names in the syntax tree
            var identifiers = root.DescendantNodes()
                .OfType<IdentifierNameSyntax>();

            foreach (var identifier in identifiers)
            {
                try
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(identifier);
                    var symbol = symbolInfo.Symbol;

                    if (symbol == null)
                        continue;

                    // Only process type symbols (not methods, properties, etc.)
                    if (symbol is INamedTypeSymbol typeSymbol)
                    {
                        var containingNamespace = typeSymbol.ContainingNamespace?.ToDisplayString();
                        
                        if (string.IsNullOrEmpty(containingNamespace))
                            continue;

                        // Check if this type belongs to one of our package namespaces
                        foreach (var ns in packageNamespaces)
                        {
                            if (containingNamespace.StartsWith(ns, StringComparison.OrdinalIgnoreCase))
                            {
                                var typeName = typeSymbol.Name;
                                packageUsage.TypesByNamespace[ns].Add(typeName);
                                packageUsage.UsedTypes.Add(typeName);
                                
                                // Track type details for migration analysis
                                if (!packageUsage.TypeDetails.ContainsKey(typeName))
                                {
                                    packageUsage.TypeDetails[typeName] = new TypeDetail
                                    {
                                        Name = typeName,
                                        Namespace = containingNamespace,
                                        AssemblyName = typeSymbol.ContainingAssembly?.Name
                                    };
                                }
                                
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // Skip symbols that can't be resolved
                }
            }

            // Also check generic names
            var genericNames = root.DescendantNodes()
                .OfType<GenericNameSyntax>();

            foreach (var genericName in genericNames)
            {
                try
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(genericName);
                    var symbol = symbolInfo.Symbol;

                    if (symbol is INamedTypeSymbol typeSymbol)
                    {
                        var containingNamespace = typeSymbol.ContainingNamespace?.ToDisplayString();
                        
                        if (string.IsNullOrEmpty(containingNamespace))
                            continue;

                        foreach (var ns in packageNamespaces)
                        {
                            if (containingNamespace.StartsWith(ns, StringComparison.OrdinalIgnoreCase))
                            {
                                var typeName = typeSymbol.Name;
                                packageUsage.TypesByNamespace[ns].Add(typeName);
                                packageUsage.UsedTypes.Add(typeName);
                                
                                // Track type details
                                if (!packageUsage.TypeDetails.ContainsKey(typeName))
                                {
                                    packageUsage.TypeDetails[typeName] = new TypeDetail
                                    {
                                        Name = typeName,
                                        Namespace = containingNamespace,
                                        AssemblyName = typeSymbol.ContainingAssembly?.Name
                                    };
                                }
                                
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // Skip symbols that can't be resolved
                }
            }

            // Check base types in class/interface declarations
            var typeDeclarations = root.DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>();

            foreach (var typeDecl in typeDeclarations)
            {
                if (typeDecl.BaseList != null)
                {
                    foreach (var baseType in typeDecl.BaseList.Types)
                    {
                        try
                        {
                            var typeInfo = semanticModel.GetTypeInfo(baseType.Type);
                            var symbol = typeInfo.Type as INamedTypeSymbol;

                            if (symbol != null)
                            {
                                var containingNamespace = symbol.ContainingNamespace?.ToDisplayString();
                                
                                if (string.IsNullOrEmpty(containingNamespace))
                                    continue;

                                foreach (var ns in packageNamespaces)
                                {
                                    if (containingNamespace.StartsWith(ns, StringComparison.OrdinalIgnoreCase))
                                    {
                                        var typeName = symbol.Name;
                                        packageUsage.TypesByNamespace[ns].Add(typeName);
                                        packageUsage.UsedTypes.Add(typeName);
                                        
                                        // Track type details
                                        if (!packageUsage.TypeDetails.ContainsKey(typeName))
                                        {
                                            packageUsage.TypeDetails[typeName] = new TypeDetail
                                            {
                                                Name = typeName,
                                                Namespace = containingNamespace,
                                                AssemblyName = symbol.ContainingAssembly?.Name
                                            };
                                        }
                                        
                                        break;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Skip types that can't be resolved
                        }
                    }
                }
            }
            
            // Now analyze dependencies for migration compatibility
            AnalyzeMigrationCompatibility(root, semanticModel, packageUsage);
        }

        static void AnalyzeMigrationCompatibility(SyntaxNode root, SemanticModel semanticModel, PackageUsageDetail packageUsage)
        {
            var frameworkSpecificNamespaces = GetFrameworkSpecificNamespaces();
            var compilation = semanticModel.Compilation;
            
            // Analyze the package assembly itself
            var packageAssemblyInfo = AnalyzePackageAssembly(packageUsage.PackageName, compilation);
            packageUsage.PackageTargetFramework = packageAssemblyInfo.TargetFramework;
            packageUsage.PackageAssemblyDependencies = packageAssemblyInfo.Dependencies;
            
            foreach (var typeDetail in packageUsage.TypeDetails.Values)
            {
                var dependencies = new HashSet<string>();
                
                // Get all type symbols used in the code
                var allIdentifiers = root.DescendantNodes()
                    .OfType<IdentifierNameSyntax>();

                foreach (var identifier in allIdentifiers)
                {
                    try
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(identifier);
                        var symbol = symbolInfo.Symbol;

                        if (symbol == null)
                            continue;

                        var containingNamespace = symbol.ContainingNamespace?.ToDisplayString();
                        var assemblyName = symbol.ContainingAssembly?.Name;

                        if (!string.IsNullOrEmpty(containingNamespace) && !string.IsNullOrEmpty(assemblyName))
                        {
                            // Check if this is a framework-specific dependency
                            foreach (var fwNs in frameworkSpecificNamespaces)
                            {
                                if (containingNamespace.StartsWith(fwNs.Key, StringComparison.OrdinalIgnoreCase))
                                {
                                    dependencies.Add($"{containingNamespace} ({fwNs.Value})");
                                    break;
                                }
                            }
                            
                            // Check if assembly itself is framework-only
                            var isFrameworkOnlyAssembly = IsFrameworkOnlyAssembly(assemblyName);
                            if (isFrameworkOnlyAssembly != null)
                            {
                                dependencies.Add($"{assemblyName} ({isFrameworkOnlyAssembly})");
                            }
                        }
                    }
                    catch
                    {
                        // Skip
                    }
                }

                typeDetail.FrameworkDependencies = dependencies.ToList();
                typeDetail.IsNetStandardCompatible = !dependencies.Any() && 
                    !packageUsage.PackageAssemblyDependencies.Any(d => d.IsFrameworkOnly);
            }
        }

        static AssemblyAnalysisInfo AnalyzePackageAssembly(string packageName, Compilation compilation)
        {
            var info = new AssemblyAnalysisInfo
            {
                PackageName = packageName
            };

            try
            {
                // Find the assembly in the compilation references
                var packageAssembly = compilation.References
                    .OfType<PortableExecutableReference>()
                    .Select(r => compilation.GetAssemblyOrModuleSymbol(r) as IAssemblySymbol)
                    .FirstOrDefault(a => a != null && a.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));

                if (packageAssembly != null)
                {
                    // Try to determine target framework from assembly metadata
                    var targetFrameworkAttr = packageAssembly.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.Name == "TargetFrameworkAttribute");
                    
                    if (targetFrameworkAttr != null && targetFrameworkAttr.ConstructorArguments.Length > 0)
                    {
                        info.TargetFramework = targetFrameworkAttr.ConstructorArguments[0].Value?.ToString() ?? "Unknown";
                    }

                    // Get assembly references (dependencies)
                    foreach (var module in packageAssembly.Modules)
                    {
                        foreach (var referencedAssembly in module.ReferencedAssemblies)
                        {
                            var depName = referencedAssembly.Name;
                            var isFrameworkOnly = IsFrameworkOnlyAssembly(depName);
                            
                            info.Dependencies.Add(new AssemblyDependency
                            {
                                Name = depName,
                                Version = referencedAssembly.Version.ToString(),
                                IsFrameworkOnly = isFrameworkOnly != null,
                                FrameworkOnlyReason = isFrameworkOnly
                            });
                        }
                    }
                }
            }
            catch
            {
                // Can't analyze assembly
            }

            return info;
        }

        static void SafeAdd(Dictionary<string, string> dict, string key, string value)
        {
            if (!dict.ContainsKey(key))
                dict[key] = value;
        }

        static string IsFrameworkOnlyAssembly(string assemblyName)
        {
            var frameworkOnlyAssemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Entity Framework 6.x
                { "EntityFramework", "Entity Framework 6.x (Use EF Core for .NET Standard)" },
                { "EntityFramework.SqlServer", "Entity Framework 6.x" },
                { "EntityFramework.SqlServerCompact", "Entity Framework 6.x" },
                
                // ASP.NET (Classic)
                { "System.Web", "ASP.NET (Classic) - Migrate to ASP.NET Core" },
                { "System.Web.Mvc", "ASP.NET MVC - Migrate to ASP.NET Core MVC" },
                { "System.Web.Http", "ASP.NET Web API - Migrate to ASP.NET Core Web API" },
                { "System.Web.WebPages", "ASP.NET Web Pages - Migrate to Razor Pages" },
                { "System.Web.Razor", "ASP.NET Razor (Classic)" },
                { "System.Web.Optimization", "ASP.NET Bundling (.NET Framework only)" },
                { "System.Web.Extensions", "ASP.NET AJAX Extensions (.NET Framework only)" },
                { "System.Web.Services", "ASP.NET Web Services (ASMX) - Use Web API/gRPC" },
                { "System.Web.ApplicationServices", "ASP.NET Application Services (.NET Framework only)" },
                { "System.Web.DynamicData", "ASP.NET Dynamic Data (.NET Framework only)" },
                { "System.Web.Entity", "ASP.NET Entity Data Source (.NET Framework only)" },
                { "System.Web.Routing", "ASP.NET Routing (Classic) - Built into ASP.NET Core" },
                
                // WCF
                { "System.ServiceModel", "WCF - Consider gRPC or ASP.NET Core Web API" },
                { "System.ServiceModel.Web", "WCF Web HTTP - Migrate to ASP.NET Core Web API" },
                { "System.ServiceModel.Activation", "WCF Activation (.NET Framework only)" },
                
                // Windows-specific
                { "System.Windows.Forms", "Windows Forms" },
                { "PresentationCore", "WPF" },
                { "PresentationFramework", "WPF" },
                { "WindowsBase", "WPF" },
                
                // Configuration
                { "System.Configuration", ".NET Framework Configuration - Use Microsoft.Extensions.Configuration" },
                { "System.Configuration.ConfigurationManager", "ConfigurationManager (Limited .NET Standard support)" },
                
                // SignalR (old)
                { "Microsoft.AspNet.SignalR", "SignalR (Classic) - Migrate to ASP.NET Core SignalR" },
                { "Microsoft.AspNet.SignalR.Core", "SignalR (Classic)" },
                { "Microsoft.AspNet.SignalR.SystemWeb", "SignalR SystemWeb (.NET Framework only)" },
                
                // Identity (old)
                { "Microsoft.AspNet.Identity", "ASP.NET Identity (Classic) - Migrate to ASP.NET Core Identity" },
                { "Microsoft.AspNet.Identity.Core", "ASP.NET Identity (Classic)" },
                { "Microsoft.AspNet.Identity.EntityFramework", "ASP.NET Identity EF (Classic)" },
                { "Microsoft.AspNet.Identity.Owin", "ASP.NET Identity OWIN (Classic)" },
                
                // OWIN
                { "Microsoft.Owin", "OWIN (.NET Framework only)" },
                { "Microsoft.Owin.Host.SystemWeb", "OWIN SystemWeb Host (.NET Framework only)" },
                { "Microsoft.Owin.Security", "OWIN Security (.NET Framework only)" },
                { "Microsoft.Owin.Security.OAuth", "OWIN OAuth (.NET Framework only)" },
                { "Microsoft.Owin.Security.Cookies", "OWIN Cookie Auth (.NET Framework only)" },
                { "Owin", "OWIN interface (.NET Framework only)" },
                
                // Logging (framework-only versions)
                { "log4net", "log4net (versions < 2.0.8 are .NET Framework only)" },
                { "Common.Logging", "Common.Logging (.NET Framework only - Use Microsoft.Extensions.Logging)" },
                { "Common.Logging.Core", "Common.Logging (.NET Framework only)" },
                
                // IoC/DI Containers (old framework-only versions)
                { "Microsoft.Practices.Unity", "Unity (old) - Use Microsoft.Extensions.DependencyInjection or Unity 5+" },
                { "Microsoft.Practices.Unity.Configuration", "Unity Configuration (old, .NET Framework only)" },
                { "Microsoft.Practices.Unity.Interception", "Unity Interception (old, .NET Framework only)" },
                { "Ninject", "Ninject (versions < 3.3 are .NET Framework only)" },
                { "Ninject.Web.Common", "Ninject Web Common (.NET Framework only)" },
                { "StructureMap", "StructureMap (.NET Framework only - Use Lamar for .NET Core)" },
                { "Spring.Core", "Spring.NET (.NET Framework only)" },
                { "Spring.Web", "Spring.NET Web (.NET Framework only)" },
                { "Spring.Data", "Spring.NET Data (.NET Framework only)" },
                
                // ORM (framework-only versions)
                { "NHibernate", "NHibernate (versions < 5.2 are .NET Framework only)" },
                { "FluentNHibernate", "FluentNHibernate (.NET Framework only)" },
                { "Iesi.Collections", "Iesi.Collections (NHibernate dependency, .NET Framework only)" },
                
                // Enterprise Library
                { "EnterpriseLibrary", "Enterprise Library (.NET Framework only)" },
                { "Microsoft.Practices.EnterpriseLibrary.Common", "Enterprise Library (.NET Framework only)" },
                { "Microsoft.Practices.EnterpriseLibrary.Data", "Enterprise Library Data (.NET Framework only)" },
                { "Microsoft.Practices.EnterpriseLibrary.Logging", "Enterprise Library Logging (.NET Framework only)" },
                { "Microsoft.Practices.EnterpriseLibrary.ExceptionHandling", "Enterprise Library (.NET Framework only)" },
                { "Microsoft.Practices.EnterpriseLibrary.Caching", "Enterprise Library Caching (.NET Framework only)" },
                { "Microsoft.Practices.EnterpriseLibrary.Validation", "Enterprise Library Validation (.NET Framework only)" },
                { "Microsoft.Practices.EnterpriseLibrary.Security", "Enterprise Library Security (.NET Framework only)" },
                { "Microsoft.Practices.EnterpriseLibrary.PolicyInjection", "Enterprise Library (.NET Framework only)" },
                
                // Windows Azure (legacy)
                { "Microsoft.WindowsAzure.Storage", "Legacy Azure Storage - Use Azure.Storage.Blobs" },
                { "Microsoft.WindowsAzure.ConfigurationManager", "Legacy Azure Configuration" },
                { "Microsoft.WindowsAzure.ServiceRuntime", "Azure Cloud Services Runtime (.NET Framework only)" },
                { "Microsoft.WindowsAzure.Diagnostics", "Azure Cloud Services Diagnostics (.NET Framework only)" },
                
                // ASP.NET Web Optimization / Bundling
                { "Microsoft.AspNet.Web.Optimization", "ASP.NET Bundling (.NET Framework only)" },
                { "WebGrease", "WebGrease (.NET Framework only)" },
                { "Antlr3.Runtime", "ANTLR 3 (Classic, bundled with ASP.NET)" },
                
                // Misc .NET Framework-only
                { "Microsoft.Web.Infrastructure", "Microsoft.Web.Infrastructure (.NET Framework only)" },
                { "Microsoft.CodeDom.Providers.DotNetCompilerPlatform", "Roslyn CodeDom (.NET Framework only)" },
                { "Microsoft.Net.Compilers", ".NET Framework Compilers (not needed in .NET Core)" },
                { "DotNetOpenAuth", "DotNetOpenAuth (.NET Framework only)" },
                { "DotNetOpenAuth.Core", "DotNetOpenAuth (.NET Framework only)" },
                
                // Reporting
                { "Microsoft.ReportViewer.WebForms", "Report Viewer WebForms (.NET Framework only)" },
                { "Microsoft.ReportViewer.WinForms", "Report Viewer WinForms (.NET Framework only)" },
                { "Microsoft.ReportViewer.Common", "Report Viewer (.NET Framework only)" },
                { "CrystalDecisions.CrystalReports.Engine", "Crystal Reports (.NET Framework only)" },
                { "CrystalDecisions.Shared", "Crystal Reports (.NET Framework only)" },
                
                // Remoting / Runtime
                { "System.Runtime.Remoting", ".NET Remoting (.NET Framework only)" },
                { "System.EnterpriseServices", "COM+ Enterprise Services (.NET Framework only)" },
                
                // Workflow
                { "System.Workflow.Runtime", "Windows Workflow Foundation 3.x (.NET Framework only)" },
                { "System.Workflow.Activities", "Windows Workflow Foundation 3.x (.NET Framework only)" },
                { "System.Workflow.ComponentModel", "Windows Workflow Foundation 3.x (.NET Framework only)" },
                { "System.Activities", "Windows Workflow Foundation 4.x (.NET Framework only)" },
                { "System.Activities.Core.Presentation", "WF Designer (.NET Framework only)" },
                
                // Transactions (old)
                { "System.Transactions", ".NET Framework Transactions - Use System.Transactions in .NET Core 3.0+" }
            };

            return frameworkOnlyAssemblies.ContainsKey(assemblyName) 
                ? frameworkOnlyAssemblies[assemblyName] 
                : null;
        }

        static Dictionary<string, string> GetFrameworkSpecificNamespaces()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Entity Framework 6.x (Framework-only)
                { "System.Data.Entity", "Entity Framework 6.x" },
                { "EntityFramework", "Entity Framework 6.x" },
                
                // ASP.NET (Classic)
                { "System.Web", "ASP.NET (Classic)" },
                { "System.Web.Mvc", "ASP.NET MVC" },
                { "System.Web.Http", "ASP.NET Web API" },
                { "System.Web.UI", "ASP.NET Web Forms" },
                
                // WCF
                { "System.ServiceModel", "WCF (Windows Communication Foundation)" },
                
                // Windows-specific
                { "System.Windows.Forms", "Windows Forms" },
                { "System.Windows", "WPF" },
                { "System.Windows.Controls", "WPF" },
                { "System.Xaml", "XAML (WPF)" },
                
                // Configuration (old style)
                { "System.Configuration", ".NET Framework Configuration" },
                
                // Remoting
                { "System.Runtime.Remoting", ".NET Remoting" },
                
                // Enterprise Services
                { "System.EnterpriseServices", "COM+ Services" },
                
                // Workflow
                { "System.Workflow", "Windows Workflow Foundation" },
                { "System.Activities", "Windows Workflow Foundation" },
                
                // ClickOnce
                { "System.Deployment", "ClickOnce Deployment" },
                
                // DirectoryServices
                { "System.DirectoryServices", "Active Directory (Limited in .NET Standard)" },
                
                // Drawing (limited support)
                { "System.Drawing", "System.Drawing (Limited .NET Core support)" }
            };
        }

        static Dictionary<string, string> GetFrameworkOnlyNuGetPackages()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Logging
            SafeAdd(dict, "log4net", "log4net (versions < 2.0.8 are .NET Framework only) - Use Microsoft.Extensions.Logging / Serilog / NLog");
            SafeAdd(dict, "Common.Logging", "Common.Logging (.NET Framework only) - Use Microsoft.Extensions.Logging");
            SafeAdd(dict, "Common.Logging.Core", "Common.Logging (.NET Framework only)");
            SafeAdd(dict, "Common.Logging.Log4Net", "Common.Logging log4net adapter (.NET Framework only)");
            SafeAdd(dict, "Common.Logging.NLog", "Common.Logging NLog adapter (.NET Framework only)");

            // ORM / Data Access
            SafeAdd(dict, "EntityFramework", "Entity Framework 6.x (versions < 6.3 .NET Framework only) - Use EF Core");
            SafeAdd(dict, "EntityFramework.SqlServer", "Entity Framework 6.x (.NET Framework only)");
            SafeAdd(dict, "EntityFramework.SqlServerCompact", "Entity Framework 6.x (.NET Framework only)");
            SafeAdd(dict, "NHibernate", "NHibernate (versions < 5.2 .NET Framework only) - Use EF Core or upgrade NHibernate");
            SafeAdd(dict, "FluentNHibernate", "FluentNHibernate (.NET Framework only)");
            SafeAdd(dict, "Iesi.Collections", "Iesi.Collections - NHibernate dependency (.NET Framework only)");
            SafeAdd(dict, "LinqToExcel", "LinqToExcel (.NET Framework only)");

            // IoC / Dependency Injection (old framework-only versions)
            SafeAdd(dict, "Microsoft.Practices.Unity", "Unity (old) - Use Microsoft.Extensions.DependencyInjection or Unity 5+");
            SafeAdd(dict, "Microsoft.Practices.Unity.Configuration", "Unity Configuration (old, .NET Framework only)");
            SafeAdd(dict, "Microsoft.Practices.Unity.Interception", "Unity Interception (old, .NET Framework only)");
            SafeAdd(dict, "Unity.Mvc", "Unity MVC integration (.NET Framework only)");
            SafeAdd(dict, "Unity.WebApi", "Unity Web API integration (.NET Framework only)");
            SafeAdd(dict, "Ninject", "Ninject (versions < 3.3 .NET Framework only)");
            SafeAdd(dict, "Ninject.Web.Common", "Ninject Web (.NET Framework only)");
            SafeAdd(dict, "Ninject.MVC3", "Ninject MVC (.NET Framework only)");
            SafeAdd(dict, "Ninject.MVC5", "Ninject MVC5 (.NET Framework only)");
            SafeAdd(dict, "StructureMap", "StructureMap (.NET Framework only) - Use Lamar for .NET Core");
            SafeAdd(dict, "StructureMap.MVC5", "StructureMap MVC5 (.NET Framework only)");
            SafeAdd(dict, "Spring.Core", "Spring.NET (.NET Framework only)");
            SafeAdd(dict, "Spring.Web", "Spring.NET Web (.NET Framework only)");
            SafeAdd(dict, "Spring.Data", "Spring.NET Data (.NET Framework only)");
            SafeAdd(dict, "Spring.Aop", "Spring.NET AOP (.NET Framework only)");

            // ASP.NET Classic
            SafeAdd(dict, "Microsoft.AspNet.Mvc", "ASP.NET MVC (Classic) - Migrate to ASP.NET Core MVC");
            SafeAdd(dict, "Microsoft.AspNet.WebApi", "ASP.NET Web API (Classic) - Migrate to ASP.NET Core");
            SafeAdd(dict, "Microsoft.AspNet.WebApi.Core", "ASP.NET Web API (Classic)");
            SafeAdd(dict, "Microsoft.AspNet.WebApi.Client", "ASP.NET Web API Client (Classic)");
            SafeAdd(dict, "Microsoft.AspNet.WebApi.WebHost", "ASP.NET Web API WebHost (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.WebApi.Owin", "ASP.NET Web API OWIN (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.WebPages", "ASP.NET Web Pages (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.Razor", "ASP.NET Razor (Classic, .NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.SignalR", "SignalR (Classic) - Migrate to ASP.NET Core SignalR");
            SafeAdd(dict, "Microsoft.AspNet.SignalR.Core", "SignalR (Classic)");
            SafeAdd(dict, "Microsoft.AspNet.SignalR.SystemWeb", "SignalR SystemWeb (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.SignalR.JS", "SignalR JS (Classic)");
            SafeAdd(dict, "Microsoft.AspNet.Identity.Core", "ASP.NET Identity (Classic) - Migrate to ASP.NET Core Identity");
            SafeAdd(dict, "Microsoft.AspNet.Identity.EntityFramework", "ASP.NET Identity EF (Classic)");
            SafeAdd(dict, "Microsoft.AspNet.Identity.Owin", "ASP.NET Identity OWIN (Classic)");
            SafeAdd(dict, "Microsoft.AspNet.Web.Optimization", "ASP.NET Bundling (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.FriendlyUrls", "ASP.NET Friendly URLs (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.Providers", "ASP.NET Universal Providers (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.Membership.OpenAuth", "ASP.NET OpenAuth (.NET Framework only)");

            // OWIN
            SafeAdd(dict, "Microsoft.Owin", "OWIN (.NET Framework only) - Middleware built into ASP.NET Core");
            SafeAdd(dict, "Microsoft.Owin.Host.SystemWeb", "OWIN SystemWeb Host (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Host.HttpListener", "OWIN HttpListener Host (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Security", "OWIN Security (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Security.OAuth", "OWIN OAuth (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Security.Cookies", "OWIN Cookie Auth (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Security.ActiveDirectory", "OWIN AD Auth (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Security.Google", "OWIN Google Auth (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Security.Facebook", "OWIN Facebook Auth (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Security.MicrosoftAccount", "OWIN Microsoft Auth (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Security.Twitter", "OWIN Twitter Auth (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Cors", "OWIN CORS (.NET Framework only)");
            SafeAdd(dict, "Owin", "OWIN interface (.NET Framework only)");

            // Enterprise Library
            SafeAdd(dict, "EnterpriseLibrary.Common", "Enterprise Library (.NET Framework only)");
            SafeAdd(dict, "EnterpriseLibrary.Data", "Enterprise Library Data (.NET Framework only) - Use Dapper / EF Core");
            SafeAdd(dict, "EnterpriseLibrary.Logging", "Enterprise Library Logging (.NET Framework only) - Use Microsoft.Extensions.Logging");
            SafeAdd(dict, "EnterpriseLibrary.ExceptionHandling", "Enterprise Library Exception Handling (.NET Framework only)");
            SafeAdd(dict, "EnterpriseLibrary.Caching", "Enterprise Library Caching (.NET Framework only) - Use IMemoryCache");
            SafeAdd(dict, "EnterpriseLibrary.Validation", "Enterprise Library Validation (.NET Framework only) - Use FluentValidation");
            SafeAdd(dict, "EnterpriseLibrary.Security.Cryptography", "Enterprise Library Crypto (.NET Framework only)");
            SafeAdd(dict, "EnterpriseLibrary.PolicyInjection", "Enterprise Library Policy Injection (.NET Framework only)");
            SafeAdd(dict, "EnterpriseLibrary.TransientFaultHandling", "Enterprise Library Transient Fault Handling (.NET Framework only) - Use Polly");
            SafeAdd(dict, "Microsoft.Practices.EnterpriseLibrary.Common", "Enterprise Library (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Practices.EnterpriseLibrary.Data", "Enterprise Library Data (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Practices.EnterpriseLibrary.Logging", "Enterprise Library Logging (.NET Framework only)");

            // Azure (legacy SDKs)
            SafeAdd(dict, "Microsoft.WindowsAzure.Storage", "Legacy Azure Storage - Use Azure.Storage.Blobs");
            SafeAdd(dict, "Microsoft.WindowsAzure.ConfigurationManager", "Legacy Azure Configuration");
            SafeAdd(dict, "Microsoft.WindowsAzure.ServiceRuntime", "Azure Cloud Services (.NET Framework only)");
            SafeAdd(dict, "WindowsAzure.Storage", "Legacy Azure Storage - Use Azure.Storage.Blobs");
            SafeAdd(dict, "WindowsAzure.ServiceBus", "Legacy Azure Service Bus - Use Azure.Messaging.ServiceBus");

            // Web / HTTP (old)
            SafeAdd(dict, "Microsoft.Web.Infrastructure", "Microsoft.Web.Infrastructure (.NET Framework only)");
            SafeAdd(dict, "WebGrease", "WebGrease (.NET Framework only)");
            SafeAdd(dict, "Antlr", "ANTLR (Classic ASP.NET bundling dependency)");

            // Authentication (old)
            SafeAdd(dict, "DotNetOpenAuth", "DotNetOpenAuth (.NET Framework only)");
            SafeAdd(dict, "DotNetOpenAuth.Core", "DotNetOpenAuth (.NET Framework only)");
            SafeAdd(dict, "DotNetOpenAuth.AspNet", "DotNetOpenAuth ASP.NET (.NET Framework only)");

            // Build / Compilation
            SafeAdd(dict, "Microsoft.CodeDom.Providers.DotNetCompilerPlatform", "Roslyn CodeDom (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Net.Compilers", ".NET Framework Compilers (not needed in .NET Core)");
            SafeAdd(dict, "Microsoft.Net.Compilers.Toolset", ".NET Framework Compilers (not needed in .NET Core)");

            // Reporting
            SafeAdd(dict, "Microsoft.ReportViewer.WebForms", "Report Viewer WebForms (.NET Framework only)");
            SafeAdd(dict, "Microsoft.ReportViewer.WinForms", "Report Viewer WinForms (.NET Framework only)");
            SafeAdd(dict, "Microsoft.ReportViewer.Common", "Report Viewer (.NET Framework only)");
            SafeAdd(dict, "CrystalDecisions.CrystalReports.Engine", "Crystal Reports (.NET Framework only)");

            // WCF
            SafeAdd(dict, "System.ServiceModel", "WCF (.NET Framework only) - Use gRPC or ASP.NET Core Web API");

            // Misc
            SafeAdd(dict, "System.Web.Optimization", "ASP.NET Bundling (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.ScriptManager.MSAjax", "ASP.NET AJAX (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.ScriptManager.WebForms", "ASP.NET WebForms Scripts (.NET Framework only)");
            SafeAdd(dict, "AjaxControlToolkit", "ASP.NET AJAX Control Toolkit (.NET Framework only)");

            return dict;
        }

        static List<DllManifestAnalysis> AnalyzeDllManifests(
            string solutionDir,
            List<ProjectInfo> projects,
            Dictionary<string, HashSet<string>> matchedPackages,
            string[] binPaths)
        {
            var results = new List<DllManifestAnalysis>();
            var frameworkOnlyNuGets = GetFrameworkOnlyNuGetPackages();
            Console.WriteLine($"   Loaded {frameworkOnlyNuGets.Count} known .NET Framework-only NuGet entries");

            // Build search paths for DLL files
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

            var distinctDllPaths = dllSearchPaths.Distinct().ToList();
            Console.WriteLine($"   {distinctDllPaths.Count} search path(s) available for DLL lookup");
            Console.WriteLine($"   Scanning {matchedPackages.Count} matched package(s)...");
            Console.WriteLine();

            // Set up reflection-only assembly resolve handler
            ResolveEventHandler resolveHandler = (sender, args) =>
            {
                try
                {
                    return System.Reflection.Assembly.ReflectionOnlyLoad(args.Name);
                }
                catch
                {
                    // Try to find it in our search paths
                    var asmName = new AssemblyName(args.Name);
                    foreach (var searchPath in dllSearchPaths)
                    {
                        var candidatePath = Path.Combine(searchPath, asmName.Name + ".dll");
                        if (File.Exists(candidatePath))
                        {
                            try
                            {
                                return System.Reflection.Assembly.ReflectionOnlyLoadFrom(candidatePath);
                            }
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

                        // Read TargetFrameworkAttribute
                        try
                        {
                            var customAttrs = CustomAttributeData.GetCustomAttributes(assembly);
                            var tfAttr = customAttrs
                                .FirstOrDefault(a => a.AttributeType.Name == "TargetFrameworkAttribute");
                            if (tfAttr != null && tfAttr.ConstructorArguments.Count > 0)
                            {
                                analysis.TargetFramework = tfAttr.ConstructorArguments[0].Value?.ToString();
                            }
                        }
                        catch { }

                        // Get all referenced assemblies from the manifest
                        var referencedAssemblies = assembly.GetReferencedAssemblies();

                        foreach (var refAsm in referencedAssemblies)
                        {
                            var dep = new ManifestDependencyInfo
                            {
                                AssemblyName = refAsm.Name,
                                Version = refAsm.Version?.ToString() ?? "unknown"
                            };

                            // Check against framework-only assembly list
                            var assemblyBlocker = IsFrameworkOnlyAssembly(refAsm.Name);
                            if (assemblyBlocker != null)
                            {
                                dep.IsBlocker = true;
                                dep.BlockerReason = assemblyBlocker;
                                dep.BlockerCategory = "Framework Assembly";
                            }

                            // Check against framework-only NuGet package list
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
                            Console.WriteLine($"              ❌ {blockerCount} BLOCKER(S) DETECTED:");
                            Console.ResetColor();
                            foreach (var blocker in analysis.Dependencies.Where(d => d.IsBlocker))
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"                 → {blocker.AssemblyName} v{blocker.Version} ({blocker.BlockerCategory})");
                                Console.ResetColor();
                            }
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"              ✅ No blockers detected");
                            Console.ResetColor();
                        }
                    }
                    catch (Exception ex)
                    {
                        analysis.Error = ex.Message;
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"              ⚠️  Could not read manifest: {ex.Message}");
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

        static string FindPackageDll(List<string> searchPaths, string packageName)
        {
            // Try exact match first, then partial match
            foreach (var searchPath in searchPaths.Distinct())
            {
                if (!Directory.Exists(searchPath))
                    continue;

                // Exact match: packageName.dll
                var exactPath = Path.Combine(searchPath, packageName + ".dll");
                if (File.Exists(exactPath))
                    return exactPath;
            }

            // Try finding in a folder named like the package (common in NuGet packages folder)
            foreach (var searchPath in searchPaths.Distinct())
            {
                if (!Directory.Exists(searchPath))
                    continue;

                // Check if the search path itself is inside a folder matching the package name
                if (searchPath.IndexOf(packageName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var dlls = Directory.GetFiles(searchPath, "*.dll", SearchOption.TopDirectoryOnly);
                    var match = dlls.FirstOrDefault(d =>
                        Path.GetFileNameWithoutExtension(d).Equals(packageName, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        return match;
                }
            }

            return null;
        }

        static void DisplayManifestAnalysisSummary(List<DllManifestAnalysis> manifestResults)
        {
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("🔎 DLL MANIFEST DEPENDENCY ANALYSIS");
            Console.WriteLine(new string('=', 60));

            var analyzedCount = manifestResults.Count(r => r.Error == null);
            var blockerPackages = manifestResults.Where(r => r.Dependencies.Any(d => d.IsBlocker)).ToList();
            var cleanPackages = manifestResults.Where(r => r.Error == null && !r.Dependencies.Any(d => d.IsBlocker)).ToList();

            Console.WriteLine($"\n  Packages analyzed: {analyzedCount}");
            Console.WriteLine($"  With blockers:     {blockerPackages.Count}");
            Console.WriteLine($"  Clean:             {cleanPackages.Count}");

            if (blockerPackages.Any())
            {
                Console.WriteLine("\n  ❌ PACKAGES WITH .NET FRAMEWORK-ONLY DEPENDENCIES:");
                Console.WriteLine(new string('-', 50));

                foreach (var pkg in blockerPackages)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n  📦 {pkg.PackageName}");
                    Console.ResetColor();

                    if (!string.IsNullOrEmpty(pkg.TargetFramework))
                    {
                        Console.WriteLine($"     Target Framework: {pkg.TargetFramework}");
                    }

                    Console.WriteLine($"     DLL: {Path.GetFileName(pkg.DllPath)}");
                    Console.WriteLine($"     Blocker dependencies:");

                    foreach (var dep in pkg.Dependencies.Where(d => d.IsBlocker))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"       ⚠️  {dep.AssemblyName} v{dep.Version}");
                        Console.ResetColor();
                        Console.WriteLine($"          Reason: {dep.BlockerReason}");
                        Console.WriteLine($"          Category: {dep.BlockerCategory}");
                    }
                }
            }

            if (cleanPackages.Any())
            {
                Console.WriteLine("\n  ✅ PACKAGES WITH NO DETECTED BLOCKERS:");
                Console.WriteLine(new string('-', 50));

                foreach (var pkg in cleanPackages)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write($"  📦 {pkg.PackageName}");
                    Console.ResetColor();

                    if (!string.IsNullOrEmpty(pkg.TargetFramework))
                    {
                        Console.Write($"  (Target: {pkg.TargetFramework})");
                    }

                    Console.WriteLine($"  [{pkg.Dependencies.Count} deps scanned]");
                }
            }

            Console.WriteLine();
        }

        static void GenerateHtmlReport(string solutionPath, List<ProjectAnalysisResult> results, List<DllManifestAnalysis> manifestResults, string outputFile)
        {
            var html = new StringBuilder();
            var solutionName = Path.GetFileNameWithoutExtension(solutionPath);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang='en'>");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset='UTF-8'>");
            html.AppendLine("    <meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            html.AppendLine($"    <title>NuGet Usage Report - {solutionName}</title>");
            html.AppendLine("    <style>");
            html.AppendLine("        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 0; padding: 20px; background: #f5f5f5; }");
            html.AppendLine("        .container { max-width: 1400px; margin: 0 auto; background: white; padding: 30px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); border-radius: 8px; }");
            html.AppendLine("        h1 { color: #2c3e50; border-bottom: 3px solid #3498db; padding-bottom: 15px; margin-top: 0; }");
            html.AppendLine("        .header-info { color: #7f8c8d; font-size: 14px; margin-bottom: 30px; }");
            html.AppendLine("        .project { margin-bottom: 40px; border: 1px solid #e0e0e0; border-radius: 6px; overflow: hidden; }");
            html.AppendLine("        .project-header { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 20px; cursor: pointer; }");
            html.AppendLine("        .project-header:hover { opacity: 0.95; }");
            html.AppendLine("        .project-header h2 { margin: 0; font-size: 22px; }");
            html.AppendLine("        .project-stats { margin-top: 8px; font-size: 14px; opacity: 0.9; }");
            html.AppendLine("        .project-content { padding: 20px; background: #fafafa; }");
            html.AppendLine("        .package { background: white; margin-bottom: 15px; border-radius: 4px; border-left: 4px solid #3498db; overflow: hidden; }");
            html.AppendLine("        .package-header { padding: 15px; background: white; cursor: pointer; display: flex; justify-content: space-between; align-items: center; }");
            html.AppendLine("        .package-header:hover { background: #f8f9fa; }");
            html.AppendLine("        .package-name { font-weight: 600; color: #2c3e50; font-size: 16px; }");
            html.AppendLine("        .package-version { color: #7f8c8d; font-size: 14px; margin-left: 10px; }");
            html.AppendLine("        .package-stats { display: flex; gap: 20px; font-size: 13px; color: #95a5a6; }");
            html.AppendLine("        .package-detail { padding: 0 15px 15px 15px; background: #f8f9fa; display: none; }");
            html.AppendLine("        .package-detail.show { display: block; }");
            html.AppendLine("        .namespace-list { list-style: none; padding: 0; margin: 10px 0; }");
            html.AppendLine("        .namespace-item { padding: 8px 12px; background: white; margin: 5px 0; border-radius: 3px; font-family: 'Courier New', monospace; font-size: 13px; color: #27ae60; border-left: 3px solid #27ae60; }");
            html.AppendLine("        .type-list { list-style: none; padding: 0; margin: 10px 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 5px; }");
            html.AppendLine("        .type-item { padding: 6px 10px; background: #e3f2fd; margin: 0; border-radius: 3px; font-family: 'Courier New', monospace; font-size: 12px; color: #1976d2; border-left: 3px solid #1976d2; }");
            html.AppendLine("        .section-header { font-weight: 600; color: #2c3e50; margin-top: 15px; margin-bottom: 10px; font-size: 14px; }");
            html.AppendLine("        .file-list { margin-top: 15px; }");
            html.AppendLine("        .file-item { display: inline-block; padding: 4px 10px; background: #ecf0f1; margin: 3px; border-radius: 3px; font-size: 12px; color: #34495e; }");
            html.AppendLine("        .warning { background: #fff3cd; border-left-color: #ffc107; padding: 15px; color: #856404; font-weight: 500; }");
            html.AppendLine("        .toggle-icon { font-size: 20px; transition: transform 0.3s; }");
            html.AppendLine("        .toggle-icon.open { transform: rotate(90deg); }");
            html.AppendLine("        .summary { background: #e8f4f8; padding: 20px; border-radius: 6px; margin-bottom: 30px; border-left: 4px solid #3498db; }");
            html.AppendLine("        .summary h3 { margin-top: 0; color: #2c3e50; }");
            html.AppendLine("        .summary-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 15px; margin-top: 15px; }");
            html.AppendLine("        .summary-item { background: white; padding: 15px; border-radius: 4px; text-align: center; }");
            html.AppendLine("        .summary-number { font-size: 32px; font-weight: bold; color: #3498db; }");
            html.AppendLine("        .summary-label { color: #7f8c8d; font-size: 14px; margin-top: 5px; }");
            html.AppendLine("        .type-index-section { margin-top: 50px; padding-top: 30px; border-top: 3px solid #e0e0e0; }");
            html.AppendLine("        .type-index-title { color: #2c3e50; font-size: 28px; margin-bottom: 10px; }");
            html.AppendLine("        .type-index-description { color: #7f8c8d; margin-bottom: 20px; }");
            html.AppendLine("        .type-index-search { margin-bottom: 20px; }");
            html.AppendLine("        .type-index-search input { width: 100%; padding: 12px 20px; font-size: 16px; border: 2px solid #e0e0e0; border-radius: 6px; outline: none; }");
            html.AppendLine("        .type-index-search input:focus { border-color: #3498db; }");
            html.AppendLine("        .type-index-list { }");
            html.AppendLine("        .type-index-item { background: white; margin-bottom: 10px; border-radius: 6px; border: 1px solid #e0e0e0; overflow: hidden; }");
            html.AppendLine("        .type-index-header { padding: 15px 20px; cursor: pointer; display: flex; justify-content: space-between; align-items: center; background: linear-gradient(to right, #f8f9fa, white); }");
            html.AppendLine("        .type-index-header:hover { background: linear-gradient(to right, #e9ecef, #f8f9fa); }");
            html.AppendLine("        .type-index-name { font-family: 'Courier New', monospace; font-size: 16px; font-weight: 600; color: #1976d2; }");
            html.AppendLine("        .type-index-summary { display: flex; gap: 15px; align-items: center; }");
            html.AppendLine("        .type-badge { background: #e3f2fd; padding: 4px 12px; border-radius: 12px; font-size: 12px; color: #1976d2; font-weight: 500; }");
            html.AppendLine("        .type-index-detail { display: none; padding: 20px; background: #fafafa; border-top: 1px solid #e0e0e0; }");
            html.AppendLine("        .type-usage-package { background: #fff3cd; padding: 12px; border-radius: 4px; margin-bottom: 15px; font-size: 14px; color: #856404; }");
            html.AppendLine("        .type-usage-project { margin-bottom: 15px; padding: 12px; background: white; border-radius: 4px; border-left: 3px solid #3498db; }");
            html.AppendLine("        .project-label { font-weight: 600; color: #2c3e50; display: block; margin-bottom: 8px; }");
            html.AppendLine("        .type-usage-files { display: flex; flex-wrap: wrap; gap: 5px; margin-top: 8px; }");
            html.AppendLine("        .migration-section { margin-top: 50px; padding-top: 30px; border-top: 3px solid #e0e0e0; }");
            html.AppendLine("        .migration-title { color: #2c3e50; font-size: 28px; margin-bottom: 10px; }");
            html.AppendLine("        .migration-description { color: #7f8c8d; margin-bottom: 20px; }");
            html.AppendLine("        .migration-summary { background: #e8f4f8; padding: 20px; border-radius: 6px; margin-bottom: 30px; }");
            html.AppendLine("        .migration-stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(250px, 1fr)); gap: 20px; }");
            html.AppendLine("        .migration-stat { text-align: center; padding: 20px; background: white; border-radius: 6px; }");
            html.AppendLine("        .migration-stat.compatible { border-left: 4px solid #27ae60; }");
            html.AppendLine("        .migration-stat.blocked { border-left: 4px solid #e74c3c; }");
            html.AppendLine("        .stat-number { font-size: 48px; font-weight: bold; margin-bottom: 10px; }");
            html.AppendLine("        .migration-stat.compatible .stat-number { color: #27ae60; }");
            html.AppendLine("        .migration-stat.blocked .stat-number { color: #e74c3c; }");
            html.AppendLine("        .stat-label { color: #7f8c8d; font-size: 14px; }");
            html.AppendLine("        .migration-group { margin-bottom: 30px; }");
            html.AppendLine("        .migration-group-title { color: #2c3e50; font-size: 20px; margin-bottom: 8px; }");
            html.AppendLine("        .migration-group-desc { color: #7f8c8d; margin-bottom: 15px; font-size: 14px; }");
            html.AppendLine("        .migration-type { background: white; border: 1px solid #e0e0e0; border-radius: 6px; margin-bottom: 10px; overflow: hidden; }");
            html.AppendLine("        .migration-type-header { padding: 15px; cursor: pointer; display: flex; justify-content: space-between; align-items: center; background: #fff3cd; }");
            html.AppendLine("        .migration-type-header:hover { background: #ffe8a1; }");
            html.AppendLine("        .migration-type-name { font-family: 'Courier New', monospace; font-weight: 600; color: #856404; }");
            html.AppendLine("        .migration-type-detail { display: none; padding: 20px; background: #fafafa; border-top: 1px solid #e0e0e0; }");
            html.AppendLine("        .dependency-list { margin-top: 15px; padding: 15px; background: #fff; border-radius: 4px; border-left: 3px solid #e74c3c; }");
            html.AppendLine("        .dependency-list ul { margin: 10px 0 0 20px; }");
            html.AppendLine("        .dependency-list li { color: #c0392b; margin: 5px 0; }");
            html.AppendLine("        .compatible-types-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 10px; }");
            html.AppendLine("        .compatible-type-item { padding: 12px; background: #d4edda; border-radius: 4px; border-left: 3px solid #27ae60; }");
            html.AppendLine("        .compatible-type-name { font-family: 'Courier New', monospace; font-weight: 600; color: #155724; margin-bottom: 4px; }");
            html.AppendLine("        .compatible-type-ns { font-size: 12px; color: #6c757d; }");
            html.AppendLine("        .manifest-section { margin-top: 50px; padding-top: 30px; border-top: 3px solid #e74c3c; }");
            html.AppendLine("        .manifest-title { color: #2c3e50; font-size: 28px; margin-bottom: 10px; }");
            html.AppendLine("        .manifest-description { color: #7f8c8d; margin-bottom: 20px; }");
            html.AppendLine("        .manifest-summary { background: #fdf2f2; padding: 20px; border-radius: 6px; margin-bottom: 30px; border-left: 4px solid #e74c3c; }");
            html.AppendLine("        .manifest-stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 20px; margin-top: 15px; }");
            html.AppendLine("        .manifest-stat { text-align: center; padding: 15px; background: white; border-radius: 6px; }");
            html.AppendLine("        .manifest-stat .stat-number { font-size: 36px; }");
            html.AppendLine("        .manifest-stat.blocker .stat-number { color: #e74c3c; }");
            html.AppendLine("        .manifest-stat.clean .stat-number { color: #27ae60; }");
            html.AppendLine("        .manifest-stat.error .stat-number { color: #f39c12; }");
            html.AppendLine("        .manifest-package { background: white; border: 1px solid #e0e0e0; border-radius: 6px; margin-bottom: 15px; overflow: hidden; }");
            html.AppendLine("        .manifest-package-header { padding: 15px 20px; cursor: pointer; display: flex; justify-content: space-between; align-items: center; }");
            html.AppendLine("        .manifest-package-header:hover { background: #f8f9fa; }");
            html.AppendLine("        .manifest-package-header.has-blockers { background: #fff5f5; border-left: 4px solid #e74c3c; }");
            html.AppendLine("        .manifest-package-header.clean { background: #f0fff4; border-left: 4px solid #27ae60; }");
            html.AppendLine("        .manifest-package-header.has-error { background: #fffbeb; border-left: 4px solid #f39c12; }");
            html.AppendLine("        .manifest-package-name { font-weight: 600; font-size: 16px; color: #2c3e50; }");
            html.AppendLine("        .manifest-package-meta { font-size: 13px; color: #7f8c8d; margin-top: 4px; }");
            html.AppendLine("        .manifest-package-detail { display: none; padding: 20px; background: #fafafa; border-top: 1px solid #e0e0e0; }");
            html.AppendLine("        .manifest-dep-table { width: 100%; border-collapse: collapse; margin-top: 10px; }");
            html.AppendLine("        .manifest-dep-table th { background: #34495e; color: white; padding: 10px 15px; text-align: left; font-size: 13px; }");
            html.AppendLine("        .manifest-dep-table td { padding: 8px 15px; border-bottom: 1px solid #e0e0e0; font-size: 13px; }");
            html.AppendLine("        .manifest-dep-table tr:hover { background: #f8f9fa; }");
            html.AppendLine("        .manifest-dep-table tr.blocker-row { background: #fff5f5; }");
            html.AppendLine("        .manifest-dep-table tr.blocker-row:hover { background: #ffe8e8; }");
            html.AppendLine("        .blocker-badge { display: inline-block; padding: 2px 8px; border-radius: 10px; font-size: 11px; font-weight: 600; }");
            html.AppendLine("        .blocker-badge.red { background: #fde8e8; color: #c0392b; }");
            html.AppendLine("        .blocker-badge.green { background: #d4edda; color: #155724; }");
            html.AppendLine("        .blocker-badge.orange { background: #fff3cd; color: #856404; }");
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("    <div class='container'>");
            html.AppendLine($"        <h1>📊 NuGet Usage Report: {solutionName}</h1>");
            html.AppendLine($"        <div class='header-info'>Generated on {timestamp}</div>");

            // Summary section
            var totalProjects = results.Count;
            var totalPackages = results.SelectMany(r => r.PackageUsages)
                .Select(p => p.PackageName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var totalNamespaces = results.SelectMany(r => r.PackageUsages).SelectMany(p => p.UsedNamespaces).Distinct().Count();
            var totalTypes = results.SelectMany(r => r.PackageUsages).SelectMany(p => p.UsedTypes).Distinct().Count();
            var unusedPackages = results.SelectMany(r => r.PackageUsages).Count(p => !p.UsedNamespaces.Any());

            html.AppendLine("        <div class='summary'>");
            html.AppendLine("            <h3>Summary</h3>");
            html.AppendLine("            <div class='summary-grid'>");
            html.AppendLine($"                <div class='summary-item'><div class='summary-number'>{totalProjects}</div><div class='summary-label'>Projects</div></div>");
            html.AppendLine($"                <div class='summary-item'><div class='summary-number'>{totalPackages}</div><div class='summary-label'>Unique Packages</div></div>");
            html.AppendLine($"                <div class='summary-item'><div class='summary-number'>{totalNamespaces}</div><div class='summary-label'>Namespaces Used</div></div>");
            html.AppendLine($"                <div class='summary-item'><div class='summary-number'>{totalTypes}</div><div class='summary-label'>Types Used</div></div>");
            html.AppendLine($"                <div class='summary-item'><div class='summary-number'>{unusedPackages}</div><div class='summary-label'>Unused Packages</div></div>");
            html.AppendLine("            </div>");
            html.AppendLine("        </div>");

            // Projects
            foreach (var project in results.OrderBy(p => p.ProjectName))
            {
                if (!project.PackageUsages.Any())
                    continue;

                html.AppendLine("        <div class='project'>");
                html.AppendLine($"            <div class='project-header' onclick='toggleProject(\"{project.ProjectName}\")'>");
                html.AppendLine($"                <div>");
                html.AppendLine($"                    <h2>📁 {project.ProjectName}</h2>");
                html.AppendLine($"                    <div class='project-stats'>{project.PackageUsages.Count} package(s) referenced</div>");
                html.AppendLine($"                </div>");
                html.AppendLine($"                <span class='toggle-icon' id='icon-{project.ProjectName}'>▶</span>");
                html.AppendLine("            </div>");
                html.AppendLine($"            <div class='project-content' id='content-{project.ProjectName}' style='display:none;'>");

                foreach (var package in project.PackageUsages.OrderBy(p => p.PackageName))
                {
                    var isUnused = !package.UsedNamespaces.Any();
                    var packageId = $"{project.ProjectName}-{package.PackageName}".Replace(".", "-");

                    html.AppendLine("                <div class='package'>");
                    html.AppendLine($"                    <div class='package-header' onclick='togglePackage(\"{packageId}\")'>");
                    html.AppendLine("                        <div>");
                    html.AppendLine($"                            <span class='package-name'>📦 {package.PackageName}</span>");
                    html.AppendLine($"                            <span class='package-version'>v{package.Version}</span>");
                    html.AppendLine("                        </div>");
                    html.AppendLine("                        <div class='package-stats'>");
                    html.AppendLine($"                            <span>{package.UsedNamespaces.Count} namespace(s)</span>");
                    html.AppendLine($"                            <span>{package.UsedTypes.Count} type(s)</span>");
                    html.AppendLine($"                            <span>{package.Files.Count} file(s)</span>");
                    html.AppendLine($"                            <span class='toggle-icon' id='icon-{packageId}'>▶</span>");
                    html.AppendLine("                        </div>");
                    html.AppendLine("                    </div>");
                    html.AppendLine($"                    <div class='package-detail' id='content-{packageId}'>");

                    if (isUnused)
                    {
                        html.AppendLine("                        <div class='warning'>⚠️ This package is referenced but not used in any code files!</div>");
                    }
                    else
                    {
                        // Group and display by namespace
                        foreach (var ns in package.UsedNamespaces.OrderBy(n => n))
                        {
                            html.AppendLine($"                        <div class='section-header'>📋 Namespace: {ns}</div>");
                            
                            // Show types for this specific namespace
                            if (package.TypesByNamespace.ContainsKey(ns) && package.TypesByNamespace[ns].Any())
                            {
                                html.AppendLine("                        <ul class='type-list'>");
                                foreach (var type in package.TypesByNamespace[ns].OrderBy(t => t))
                                {
                                    html.AppendLine($"                            <li class='type-item'>{type}</li>");
                                }
                                html.AppendLine("                        </ul>");
                            }
                            else
                            {
                                html.AppendLine("                        <p style='margin-left: 15px; color: #95a5a6; font-size: 13px;'>No specific types detected (namespace imported but types not explicitly identified)</p>");
                            }
                        }

                        if (package.Files.Any())
                        {
                            html.AppendLine("                        <div class='file-list'>");
                            html.AppendLine("                            <div class='section-header'>📄 Files:</div>");
                            foreach (var file in package.Files.OrderBy(f => f))
                            {
                                html.AppendLine($"                            <span class='file-item'>{file}</span>");
                            }
                            html.AppendLine("                        </div>");
                        }
                    }

                    html.AppendLine("                    </div>");
                    html.AppendLine("                </div>");
                }

                html.AppendLine("            </div>");
                html.AppendLine("        </div>");
            }

            // Migration Compatibility Section
            html.AppendLine("        <div class='migration-section'>");
            html.AppendLine("            <h2 class='migration-title'>🚀 Migration Compatibility Analysis</h2>");
            html.AppendLine("            <p class='migration-description'>Analysis of types and their compatibility with .NET Standard 2.0 / .NET Core / .NET 5+</p>");

            var allTypeDetails = results
                .SelectMany(r => r.PackageUsages)
                .SelectMany(p => p.TypeDetails.Values)
                .GroupBy(t => t.Name)
                .Select(g => g.First())
                .OrderBy(t => t.IsNetStandardCompatible ? 1 : 0)
                .ThenBy(t => t.Name);

            var compatibleCount = allTypeDetails.Count(t => t.IsNetStandardCompatible);
            var blockedCount = allTypeDetails.Count(t => !t.IsNetStandardCompatible);

            html.AppendLine("            <div class='migration-summary'>");
            html.AppendLine("                <div class='migration-stats'>");
            html.AppendLine($"                    <div class='migration-stat compatible'>");
            html.AppendLine($"                        <div class='stat-number'>{compatibleCount}</div>");
            html.AppendLine($"                        <div class='stat-label'>✅ .NET Standard Compatible</div>");
            html.AppendLine("                    </div>");
            html.AppendLine($"                    <div class='migration-stat blocked'>");
            html.AppendLine($"                        <div class='stat-number'>{blockedCount}</div>");
            html.AppendLine($"                        <div class='stat-label'>⚠️ Framework Dependencies</div>");
            html.AppendLine("                    </div>");
            html.AppendLine("                </div>");
            html.AppendLine("            </div>");

            // Blocked types
            var blockedTypes = allTypeDetails.Where(t => !t.IsNetStandardCompatible).ToList();
            if (blockedTypes.Any())
            {
                html.AppendLine("            <div class='migration-group'>");
                html.AppendLine("                <h3 class='migration-group-title'>⚠️ Types with .NET Framework Dependencies</h3>");
                html.AppendLine("                <p class='migration-group-desc'>These types use .NET Framework-specific APIs and may require refactoring for migration.</p>");

                foreach (var typeDetail in blockedTypes)
                {
                    var migTypeId = $"mig-{typeDetail.Name.Replace("<", "-").Replace(">", "-")}";
                    html.AppendLine($"                <div class='migration-type'>");
                    html.AppendLine($"                    <div class='migration-type-header' onclick='toggleMigrationType(\"{migTypeId}\")'>");
                    html.AppendLine($"                        <div class='migration-type-name'>❌ {typeDetail.Name}</div>");
                    html.AppendLine($"                        <span class='toggle-icon' id='icon-{migTypeId}'>▶</span>");
                    html.AppendLine("                    </div>");
                    html.AppendLine($"                    <div class='migration-type-detail' id='detail-{migTypeId}'>");
                    html.AppendLine($"                        <div><strong>Namespace:</strong> {typeDetail.Namespace}</div>");
                    html.AppendLine($"                        <div><strong>Assembly:</strong> {typeDetail.AssemblyName ?? "Unknown"}</div>");
                    html.AppendLine("                        <div class='dependency-list'>");
                    html.AppendLine("                            <strong>Framework Dependencies:</strong>");
                    html.AppendLine("                            <ul>");
                    foreach (var dep in typeDetail.FrameworkDependencies.Distinct())
                    {
                        html.AppendLine($"                                <li>{dep}</li>");
                    }
                    html.AppendLine("                            </ul>");
                    html.AppendLine("                        </div>");
                    html.AppendLine("                    </div>");
                    html.AppendLine("                </div>");
                }

                html.AppendLine("            </div>");
            }

            // Compatible types
            var compatibleTypes = allTypeDetails.Where(t => t.IsNetStandardCompatible).ToList();
            if (compatibleTypes.Any())
            {
                html.AppendLine("            <div class='migration-group'>");
                html.AppendLine("                <h3 class='migration-group-title'>✅ .NET Standard Compatible Types</h3>");
                html.AppendLine("                <p class='migration-group-desc'>These types don't have detected .NET Framework dependencies and should be easier to migrate.</p>");
                html.AppendLine("                <div class='compatible-types-grid'>");
                
                foreach (var typeDetail in compatibleTypes)
                {
                    html.AppendLine($"                    <div class='compatible-type-item'>");
                    html.AppendLine($"                        <div class='compatible-type-name'>✅ {typeDetail.Name}</div>");
                    html.AppendLine($"                        <div class='compatible-type-ns'>{typeDetail.Namespace}</div>");
                    html.AppendLine("                    </div>");
                }
                
                html.AppendLine("                </div>");
                html.AppendLine("            </div>");
            }

            html.AppendLine("        </div>");

            // DLL Manifest Analysis Section
            if (manifestResults != null && manifestResults.Any())
            {
                var manifestAnalyzed = manifestResults.Count(r => r.Error == null);
                var manifestBlockers = manifestResults.Count(r => r.Dependencies.Any(d => d.IsBlocker));
                var manifestClean = manifestResults.Count(r => r.Error == null && !r.Dependencies.Any(d => d.IsBlocker));
                var manifestErrors = manifestResults.Count(r => r.Error != null);

                html.AppendLine("        <div class='manifest-section'>");
                html.AppendLine("            <h2 class='manifest-title'>🔎 DLL Manifest Dependency Analysis</h2>");
                html.AppendLine("            <p class='manifest-description'>Assembly manifest inspection of matched NuGet package DLLs to detect .NET Framework-only dependencies that block migration to .NET Standard 2.0.</p>");

                html.AppendLine("            <div class='manifest-summary'>");
                html.AppendLine("                <h3 style='margin-top:0; color: #2c3e50;'>Manifest Analysis Summary</h3>");
                html.AppendLine("                <div class='manifest-stats'>");
                html.AppendLine($"                    <div class='manifest-stat blocker'><div class='stat-number'>{manifestBlockers}</div><div class='stat-label'>❌ With Blockers</div></div>");
                html.AppendLine($"                    <div class='manifest-stat clean'><div class='stat-number'>{manifestClean}</div><div class='stat-label'>✅ Clean</div></div>");
                html.AppendLine($"                    <div class='manifest-stat error'><div class='stat-number'>{manifestErrors}</div><div class='stat-label'>⚠️ Could Not Analyze</div></div>");
                html.AppendLine("                </div>");
                html.AppendLine("            </div>");

                // Packages with blockers first
                var sortedManifests = manifestResults
                    .OrderByDescending(r => r.Dependencies.Count(d => d.IsBlocker))
                    .ThenBy(r => r.PackageName);

                foreach (var manifest in sortedManifests)
                {
                    var hasBlockers = manifest.Dependencies.Any(d => d.IsBlocker);
                    var hasError = manifest.Error != null;
                    var headerClass = hasBlockers ? "has-blockers" : hasError ? "has-error" : "clean";
                    var statusIcon = hasBlockers ? "❌" : hasError ? "⚠️" : "✅";
                    var manifestId = $"manifest-{manifest.PackageName.Replace(".", "-")}";

                    html.AppendLine($"                <div class='manifest-package'>");
                    html.AppendLine($"                    <div class='manifest-package-header {headerClass}' onclick='toggleManifest(\"{manifestId}\")'>");
                    html.AppendLine("                        <div>");
                    html.AppendLine($"                            <div class='manifest-package-name'>{statusIcon} {manifest.PackageName}</div>");

                    if (!string.IsNullOrEmpty(manifest.TargetFramework))
                    {
                        html.AppendLine($"                            <div class='manifest-package-meta'>Target Framework: {manifest.TargetFramework}</div>");
                    }

                    if (hasError)
                    {
                        html.AppendLine($"                            <div class='manifest-package-meta'>⚠️ {manifest.Error}</div>");
                    }
                    else
                    {
                        var blockerDeps = manifest.Dependencies.Count(d => d.IsBlocker);
                        var totalDeps = manifest.Dependencies.Count;
                        html.AppendLine($"                            <div class='manifest-package-meta'>{totalDeps} referenced assemblies, {blockerDeps} blocker(s)</div>");
                    }

                    html.AppendLine("                        </div>");
                    html.AppendLine($"                        <span class='toggle-icon' id='icon-{manifestId}'>▶</span>");
                    html.AppendLine("                    </div>");
                    html.AppendLine($"                    <div class='manifest-package-detail' id='detail-{manifestId}'>");

                    if (manifest.Dependencies.Any())
                    {
                        if (!string.IsNullOrEmpty(manifest.DllPath))
                        {
                            html.AppendLine($"                        <div style='margin-bottom: 15px; font-size: 13px; color: #7f8c8d;'><strong>DLL Path:</strong> {manifest.DllPath}</div>");
                        }
                        if (!string.IsNullOrEmpty(manifest.AssemblyFullName))
                        {
                            html.AppendLine($"                        <div style='margin-bottom: 15px; font-size: 13px; color: #7f8c8d;'><strong>Assembly:</strong> {manifest.AssemblyFullName}</div>");
                        }

                        html.AppendLine("                        <table class='manifest-dep-table'>");
                        html.AppendLine("                            <thead><tr><th>Referenced Assembly</th><th>Version</th><th>Status</th><th>Details</th></tr></thead>");
                        html.AppendLine("                            <tbody>");

                        foreach (var dep in manifest.Dependencies.OrderByDescending(d => d.IsBlocker).ThenBy(d => d.AssemblyName))
                        {
                            var rowClass = dep.IsBlocker ? " class='blocker-row'" : "";
                            var statusBadge = dep.IsBlocker
                                ? $"<span class='blocker-badge red'>❌ BLOCKER</span>"
                                : "<span class='blocker-badge green'>✅ OK</span>";
                            var details = dep.IsBlocker
                                ? $"<strong>{dep.BlockerCategory}:</strong> {dep.BlockerReason}"
                                : "";

                            html.AppendLine($"                                <tr{rowClass}>");
                            html.AppendLine($"                                    <td><code>{dep.AssemblyName}</code></td>");
                            html.AppendLine($"                                    <td>{dep.Version}</td>");
                            html.AppendLine($"                                    <td>{statusBadge}</td>");
                            html.AppendLine($"                                    <td>{details}</td>");
                            html.AppendLine("                                </tr>");
                        }

                        html.AppendLine("                            </tbody>");
                        html.AppendLine("                        </table>");
                    }

                    html.AppendLine("                    </div>");
                    html.AppendLine("                </div>");
                }

                html.AppendLine("        </div>");
            }

            // Type Index Section - All types across solution
            var typeIndex = BuildTypeIndex(results);
            
            if (typeIndex.Any())
            {
                html.AppendLine("        <div class='type-index-section'>");
                html.AppendLine("            <h2 class='type-index-title'>🔷 Type Index - All Types Used Across Solution</h2>");
                html.AppendLine("            <p class='type-index-description'>This section shows all unique types and where they are used throughout the solution.</p>");
                
                html.AppendLine("            <div class='type-index-search'>");
                html.AppendLine("                <input type='text' id='typeSearch' placeholder='🔍 Search for a type...' onkeyup='filterTypes()' />");
                html.AppendLine("            </div>");

                html.AppendLine("            <div class='type-index-list'>");
                
                foreach (var typeEntry in typeIndex.OrderBy(t => t.Key))
                {
                    var typeName = typeEntry.Key;
                    var usages = typeEntry.Value;
                    var typeId = $"type-{typeName.Replace("<", "-").Replace(">", "-")}";
                    
                    var typeProjectCount = usages.Select(u => u.ProjectName).Distinct().Count();
                    var typeFileCount = usages.SelectMany(u => u.Files).Distinct().Count();

                    html.AppendLine($"                <div class='type-index-item' data-typename='{typeName.ToLower()}'>");
                    html.AppendLine($"                    <div class='type-index-header' onclick='toggleTypeDetail(\"{typeId}\")'>");
                    html.AppendLine($"                        <div class='type-index-name'>{typeName}</div>");
                    html.AppendLine($"                        <div class='type-index-summary'>");
                    html.AppendLine($"                            <span class='type-badge'>📦 {usages.Select(u => u.PackageName).Distinct().Count()} package(s)</span>");
                    html.AppendLine($"                            <span class='type-badge'>📁 {typeProjectCount} project(s)</span>");
                    html.AppendLine($"                            <span class='type-badge'>📄 {typeFileCount} file(s)</span>");
                    html.AppendLine($"                            <span class='toggle-icon' id='icon-{typeId}'>▶</span>");
                    html.AppendLine($"                        </div>");
                    html.AppendLine("                    </div>");
                    html.AppendLine($"                    <div class='type-index-detail' id='detail-{typeId}'>");

                    // Group by package
                    var packageGroups = usages.GroupBy(u => u.PackageName);
                    foreach (var packageGroup in packageGroups.OrderBy(g => g.Key))
                    {
                        html.AppendLine($"                        <div class='type-usage-package'>");
                        html.AppendLine($"                            <strong>Package:</strong> {packageGroup.Key}");
                        html.AppendLine($"                            <br/><strong>Namespace:</strong> {packageGroup.First().Namespace}");
                        html.AppendLine("                        </div>");

                        // List projects using this type from this package
                        var projectGroups = packageGroup.GroupBy(u => u.ProjectName);
                        foreach (var projectGroup in projectGroups.OrderBy(g => g.Key))
                        {
                            html.AppendLine($"                        <div class='type-usage-project'>");
                            html.AppendLine($"                            <span class='project-label'>📁 {projectGroup.Key}</span>");
                            html.AppendLine("                            <div class='type-usage-files'>");
                            
                            var allFiles = projectGroup.SelectMany(u => u.Files).Distinct().OrderBy(f => f);
                            foreach (var file in allFiles)
                            {
                                html.AppendLine($"                                <span class='file-item'>{file}</span>");
                            }
                            html.AppendLine("                            </div>");
                            html.AppendLine("                        </div>");
                        }
                    }

                    html.AppendLine("                    </div>");
                    html.AppendLine("                </div>");
                }

                html.AppendLine("            </div>");
                html.AppendLine("        </div>");
            }

            html.AppendLine("    </div>");
            html.AppendLine("    <script>");
            html.AppendLine("        function toggleProject(projectName) {");
            html.AppendLine("            var content = document.getElementById('content-' + projectName);");
            html.AppendLine("            var icon = document.getElementById('icon-' + projectName);");
            html.AppendLine("            if (content.style.display === 'none') {");
            html.AppendLine("                content.style.display = 'block';");
            html.AppendLine("                icon.classList.add('open');");
            html.AppendLine("            } else {");
            html.AppendLine("                content.style.display = 'none';");
            html.AppendLine("                icon.classList.remove('open');");
            html.AppendLine("            }");
            html.AppendLine("        }");
            html.AppendLine("        function togglePackage(packageId) {");
            html.AppendLine("            var content = document.getElementById('content-' + packageId);");
            html.AppendLine("            var icon = document.getElementById('icon-' + packageId);");
            html.AppendLine("            content.classList.toggle('show');");
            html.AppendLine("            icon.classList.toggle('open');");
            html.AppendLine("        }");
            html.AppendLine("        function toggleTypeDetail(typeId) {");
            html.AppendLine("            var detail = document.getElementById('detail-' + typeId);");
            html.AppendLine("            var icon = document.getElementById('icon-' + typeId);");
            html.AppendLine("            if (detail.style.display === 'none' || !detail.style.display) {");
            html.AppendLine("                detail.style.display = 'block';");
            html.AppendLine("                icon.classList.add('open');");
            html.AppendLine("            } else {");
            html.AppendLine("                detail.style.display = 'none';");
            html.AppendLine("                icon.classList.remove('open');");
            html.AppendLine("            }");
            html.AppendLine("        }");
            html.AppendLine("        function filterTypes() {");
            html.AppendLine("            var input = document.getElementById('typeSearch').value.toLowerCase();");
            html.AppendLine("            var items = document.querySelectorAll('.type-index-item');");
            html.AppendLine("            items.forEach(function(item) {");
            html.AppendLine("                var typeName = item.getAttribute('data-typename');");
            html.AppendLine("                if (typeName.includes(input)) {");
            html.AppendLine("                    item.style.display = '';");
            html.AppendLine("                } else {");
            html.AppendLine("                    item.style.display = 'none';");
            html.AppendLine("                }");
            html.AppendLine("            });");
            html.AppendLine("        }");
            html.AppendLine("        function toggleMigrationType(typeId) {");
            html.AppendLine("            var detail = document.getElementById('detail-' + typeId);");
            html.AppendLine("            var icon = document.getElementById('icon-' + typeId);");
            html.AppendLine("            if (detail.style.display === 'none' || !detail.style.display) {");
            html.AppendLine("                detail.style.display = 'block';");
            html.AppendLine("                icon.classList.add('open');");
            html.AppendLine("            } else {");
            html.AppendLine("                detail.style.display = 'none';");
            html.AppendLine("                icon.classList.remove('open');");
            html.AppendLine("            }");
            html.AppendLine("        }");
            html.AppendLine("        function toggleManifest(manifestId) {");
            html.AppendLine("            var detail = document.getElementById('detail-' + manifestId);");
            html.AppendLine("            var icon = document.getElementById('icon-' + manifestId);");
            html.AppendLine("            if (detail.style.display === 'none' || !detail.style.display) {");
            html.AppendLine("                detail.style.display = 'block';");
            html.AppendLine("                icon.classList.add('open');");
            html.AppendLine("            } else {");
            html.AppendLine("                detail.style.display = 'none';");
            html.AppendLine("                icon.classList.remove('open');");
            html.AppendLine("            }");
            html.AppendLine("        }");
            html.AppendLine("    </script>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            File.WriteAllText(outputFile, html.ToString());
        }

        static Dictionary<string, List<TypeUsageInfo>> BuildTypeIndex(List<ProjectAnalysisResult> results)
        {
            var typeIndex = new Dictionary<string, List<TypeUsageInfo>>(StringComparer.OrdinalIgnoreCase);

            foreach (var project in results)
            {
                foreach (var package in project.PackageUsages)
                {
                    foreach (var nsEntry in package.TypesByNamespace)
                    {
                        var ns = nsEntry.Key;
                        var types = nsEntry.Value;

                        foreach (var type in types)
                        {
                            if (!typeIndex.ContainsKey(type))
                            {
                                typeIndex[type] = new List<TypeUsageInfo>();
                            }

                            typeIndex[type].Add(new TypeUsageInfo
                            {
                                TypeName = type,
                                Namespace = ns,
                                PackageName = package.PackageName,
                                ProjectName = project.ProjectName,
                                Files = package.Files.ToList()
                            });
                        }
                    }
                }
            }

            return typeIndex;
        }

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
                    // Collect all following arguments until next flag
                    for (int j = i + 1; j < args.Length && !args[j].StartsWith("-"); j++)
                    {
                        binPaths.Add(args[j]);
                        i = j;
                    }
                }
                else if (args[i] == "--dependencies" || args[i] == "-d")
                {
                    // Collect all following arguments until next flag
                    for (int j = i + 1; j < args.Length && !args[j].StartsWith("-"); j++)
                    {
                        dependencies.Add(args[j]);
                        i = j;
                    }
                }
            }

            if (string.IsNullOrEmpty(solutionPath) || !dependencies.Any())
            {
                return null;
            }

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
            Console.WriteLine("Application Crawler - Analyzes NuGet package usage in .NET solutions");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  DotNetCrawler.exe --solution <path> --dependencies <pattern1> [pattern2] ... [--output <file>] [--bin-paths <path1> <path2> ...]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -s, --solution <path>          Path to the solution file (.sln)");
            Console.WriteLine("  -d, --dependencies <patterns>  NuGet package name patterns (supports wildcards)");
            Console.WriteLine("  -o, --output <file>           Path to output HTML report (optional)");
            Console.WriteLine("  -b, --bin-paths <paths>       Paths to bin folders with compiled DLLs for accurate analysis (optional, auto-detected if not specified)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  DotNetCrawler.exe -s MySolution.sln -d \"Newtonsoft.*\" \"System.Net.Http\"");
            Console.WriteLine("  DotNetCrawler.exe --solution \"C:\\Projects\\App.sln\" --dependencies \"MyLib.*\" --output report.html");
            Console.WriteLine("  DotNetCrawler.exe -s MySolution.sln -d \"MyLib.*\" -b \"C:\\Projects\\bin\\Debug\" \"C:\\Projects\\bin\\Release\"");
        }

        class ParsedArguments
        {
            public string SolutionPath { get; set; }
            public string[] DependencyPatterns { get; set; }
            public string OutputFile { get; set; }
            public string[] BinPaths { get; set; }
        }

        class ProjectInfo
        {
            public string Name { get; set; }
            public string Path { get; set; }
            public List<Tuple<string, string>> Packages { get; set; }
        }

        class ProjectAnalysisResult
        {
            public string ProjectName { get; set; }
            public string ProjectPath { get; set; }
            public List<PackageUsageDetail> PackageUsages { get; set; }

            public ProjectAnalysisResult()
            {
                PackageUsages = new List<PackageUsageDetail>();
            }
        }

        class PackageUsageDetail
        {
            public string PackageName { get; set; }
            public string Version { get; set; }
            public HashSet<string> UsedNamespaces { get; set; }
            public Dictionary<string, HashSet<string>> TypesByNamespace { get; set; } // Namespace -> Types
            public HashSet<string> UsedTypes { get; set; } // All types combined (for backward compatibility)
            public HashSet<string> Files { get; set; }
            public Dictionary<string, TypeDetail> TypeDetails { get; set; } // Type name -> details with dependencies
            public string PackageTargetFramework { get; set; }
            public List<AssemblyDependency> PackageAssemblyDependencies { get; set; }

            public PackageUsageDetail()
            {
                UsedNamespaces = new HashSet<string>();
                TypesByNamespace = new Dictionary<string, HashSet<string>>();
                UsedTypes = new HashSet<string>();
                Files = new HashSet<string>();
                TypeDetails = new Dictionary<string, TypeDetail>();
                PackageAssemblyDependencies = new List<AssemblyDependency>();
            }
        }

        class TypeDetail
        {
            public string Name { get; set; }
            public string Namespace { get; set; }
            public string AssemblyName { get; set; }
            public List<string> FrameworkDependencies { get; set; }
            public bool IsNetStandardCompatible { get; set; }

            public TypeDetail()
            {
                FrameworkDependencies = new List<string>();
                IsNetStandardCompatible = true;
            }
        }

        class TypeUsageInfo
        {
            public string TypeName { get; set; }
            public string Namespace { get; set; }
            public string PackageName { get; set; }
            public string ProjectName { get; set; }
            public List<string> Files { get; set; }
        }

        class AssemblyAnalysisInfo
        {
            public string PackageName { get; set; }
            public string TargetFramework { get; set; }
            public List<AssemblyDependency> Dependencies { get; set; }

            public AssemblyAnalysisInfo()
            {
                Dependencies = new List<AssemblyDependency>();
            }
        }

        class AssemblyDependency
        {
            public string Name { get; set; }
            public string Version { get; set; }
            public bool IsFrameworkOnly { get; set; }
            public string FrameworkOnlyReason { get; set; }
        }

        class DllManifestAnalysis
        {
            public string PackageName { get; set; }
            public string DllPath { get; set; }
            public string AssemblyFullName { get; set; }
            public string TargetFramework { get; set; }
            public string Error { get; set; }
            public List<ManifestDependencyInfo> Dependencies { get; set; }

            public DllManifestAnalysis()
            {
                Dependencies = new List<ManifestDependencyInfo>();
            }
        }

        class ManifestDependencyInfo
        {
            public string AssemblyName { get; set; }
            public string Version { get; set; }
            public bool IsBlocker { get; set; }
            public string BlockerReason { get; set; }
            public string BlockerCategory { get; set; }
        }
    }
}
