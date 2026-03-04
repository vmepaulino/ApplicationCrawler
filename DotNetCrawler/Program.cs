using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            Console.WriteLine($"🔍 Analyzing solution: {Path.GetFileName(solutionPath)}");
            Console.WriteLine($"📦 Dependency patterns: {string.Join(", ", dependencyPatterns)}");
            Console.WriteLine();

            Console.WriteLine("⏳ Loading solution...");
            
            var solutionDir = Path.GetDirectoryName(solutionPath);
            var projectPaths = ParseSolutionFile(solutionPath);
            
            Console.WriteLine($"✅ Found {projectPaths.Count} projects");
            Console.WriteLine();

            var patterns = dependencyPatterns.Select(ConvertWildcardToRegex).ToArray();
            var matchedPackages = new Dictionary<string, HashSet<string>>(); // Package -> Projects
            var projectInfos = new List<ProjectInfo>();

            foreach (var projectPath in projectPaths)
            {
                var fullProjectPath = Path.Combine(solutionDir, projectPath);
                if (!File.Exists(fullProjectPath))
                {
                    Console.WriteLine($"⚠️  Project file not found: {projectPath}");
                    continue;
                }

                var projectName = Path.GetFileNameWithoutExtension(projectPath);
                Console.WriteLine($"📁 Project: {projectName}");

                var packages = GetNuGetPackagesFromProjectFile(fullProjectPath);

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

            Console.WriteLine(new string('=', 60));
            Console.WriteLine("📊 SUMMARY");
            Console.WriteLine(new string('=', 60));

            if (matchedPackages.Any())
            {
                Console.WriteLine($"\nFound {matchedPackages.Count} matching packages:");
                foreach (var kvp in matchedPackages.OrderBy(x => x.Key))
                {
                    Console.WriteLine($"\n  📦 {kvp.Key}");
                    Console.WriteLine($"     Used in {kvp.Value.Count} project(s): {string.Join(", ", kvp.Value)}");
                }

                Console.WriteLine("\n🔄 Next Step: Analyzing actual code usage...");
                
                // Load DLL references for semantic analysis
                var metadataReferences = LoadMetadataReferences(solutionDir, projectInfos, binPaths);
                
                var analysisResults = await AnalyzeCodeUsageByProjectAsync(projectInfos, matchedPackages.Keys.ToArray(), metadataReferences);
                
                // Display console summary
                DisplayConsoleSummary(analysisResults);
                
                // Generate HTML report
                if (!string.IsNullOrEmpty(outputFile))
                {
                    GenerateHtmlReport(solutionPath, analysisResults, outputFile);
                    Console.WriteLine($"\n📄 HTML report generated: {Path.GetFullPath(outputFile)}");
                }
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
            Console.WriteLine("\n🔧 Loading assembly references for semantic analysis...");
            
            var references = new List<MetadataReference>();
            var loadedDlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add framework references
            var frameworkPath = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
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

            var searchPaths = new List<string>();

            // Use provided bin paths
            if (binPaths != null && binPaths.Any())
            {
                searchPaths.AddRange(binPaths);
            }
            else
            {
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
                    var packageDllFolders = Directory.GetDirectories(packagesFolder, "*", SearchOption.AllDirectories)
                        .Where(d => d.Contains("\\lib\\") && !d.Contains("\\build\\"));
                    searchPaths.AddRange(packageDllFolders);
                }
            }

            // Load DLLs from all search paths
            foreach (var searchPath in searchPaths.Distinct())
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
                        Console.WriteLine($"   DLL {dllName} not loaded ! ");
                        // Skip DLLs that can't be loaded
                    }
                }
            }

            Console.WriteLine($"   ✅ Loaded {references.Count} assembly references");
            return references;
        }

        static async Task<List<ProjectAnalysisResult>> AnalyzeCodeUsageByProjectAsync(List<ProjectInfo> projects, string[] packageNames, List<MetadataReference> metadataReferences)
        {
            Console.WriteLine("\n🔬 Analyzing code usage with semantic analysis...");

            var results = new List<ProjectAnalysisResult>();

            foreach (var projectInfo in projects)
            {
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
                var syntaxTrees = new List<SyntaxTree>();
                var fileContents = new Dictionary<string, string>();

                foreach (var csFile in csFiles)
                {
                    try
                    {
                        var code = File.ReadAllText(csFile);
                        fileContents[csFile] = code;
                        var tree = CSharpSyntaxTree.ParseText(code, path: csFile);
                        syntaxTrees.Add(tree);
                    }
                    catch { /* Skip files that can't be read */ }
                }

                // Create compilation with all references
                var compilation = CSharpCompilation.Create(
                    projectInfo.Name,
                    syntaxTrees: syntaxTrees,
                    references: metadataReferences,
                    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                );

                // Analyze each file with semantic model
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
                        foreach (var packageUsage in projectResult.PackageUsages)
                        {
                            var packageNamespacesInFile = usingDirectives
                                .Where(u => u.StartsWith(packageUsage.PackageName, StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            if (packageNamespacesInFile.Any())
                            {
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
                    }
                    catch (Exception ex)
                    {
                        // Ignore files that can't be analyzed
                    }
                }

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
        }

        static void GenerateHtmlReport(string solutionPath, List<ProjectAnalysisResult> results, string outputFile)
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

            public PackageUsageDetail()
            {
                UsedNamespaces = new HashSet<string>();
                TypesByNamespace = new Dictionary<string, HashSet<string>>();
                UsedTypes = new HashSet<string>();
                Files = new HashSet<string>();
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
    }
}
