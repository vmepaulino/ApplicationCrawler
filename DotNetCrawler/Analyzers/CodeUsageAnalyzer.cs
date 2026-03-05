using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotNetCrawler.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ProjectInfo = DotNetCrawler.Models.ProjectInfo;


namespace DotNetCrawler.Analyzers
{
    internal static class CodeUsageAnalyzer
    {
        public static List<MetadataReference> LoadMetadataReferences(string solutionDir, List<ProjectInfo> projects, string[] binPaths)
        {
            var references = new List<MetadataReference>();
            var loadedDlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                    catch { }
                }
            }
            Console.WriteLine($"   ✓ {loadedDlls.Count} framework assemblies loaded");

            var searchPaths = new List<string>();

            if (binPaths != null && binPaths.Any())
            {
                Console.WriteLine($"   Using {binPaths.Length} custom bin path(s)");
                searchPaths.AddRange(binPaths);
            }
            else
            {
                Console.WriteLine("   Auto-detecting assembly search paths...");
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
                    }
                }
            }

            Console.WriteLine($"   ✅ Loaded {references.Count} assembly references");
            if (failedDlls > 0)
                Console.WriteLine($"   ⚠️  {failedDlls} DLL(s) could not be loaded");
            return references;
        }

        public static async Task<List<ProjectAnalysisResult>> AnalyzeCodeUsageByProjectAsync(
            List<ProjectInfo> projects, string[] packageNames, List<MetadataReference> metadataReferences)
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

                var matchedPackagesForProject = projectInfo.Packages
                    .Where(p => packageNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
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
                        PackageName = package.Name,
                        Version = package.Version
                    };
                    projectResult.PackageUsages.Add(packageUsage);
                }

                Console.WriteLine($"              Parsing {csFiles.Count} source file(s)...");
                var syntaxTrees = new List<SyntaxTree>();
                var parseFailures = 0;

                foreach (var csFile in csFiles)
                {
                    try
                    {
                        var code = File.ReadAllText(csFile);
                        var tree = CSharpSyntaxTree.ParseText(code, path: csFile);
                        syntaxTrees.Add(tree);
                    }
                    catch { parseFailures++; }
                }

                if (parseFailures > 0)
                    Console.WriteLine($"              ⚠️  {parseFailures} file(s) could not be parsed");

                Console.WriteLine($"              Building Roslyn compilation ({syntaxTrees.Count} trees, {metadataReferences.Count} refs)...");
                var compilation = CSharpCompilation.Create(
                    projectInfo.Name,
                    syntaxTrees: syntaxTrees,
                    references: metadataReferences,
                    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                );

                var filesWithUsage = 0;
                var analyzeFailures = 0;
                foreach (var tree in syntaxTrees)
                {
                    try
                    {
                        var semanticModel = compilation.GetSemanticModel(tree);
                        var root = await tree.GetRootAsync();

                        var usingDirectives = root.DescendantNodes()
                            .OfType<UsingDirectiveSyntax>()
                            .Select(u => u.Name.ToString())
                            .ToHashSet();

                        var fileHasUsage = false;
                        foreach (var packageUsage in projectResult.PackageUsages)
                        {
                            var packageNamespacesInFile = usingDirectives
                                .Where(u => u.StartsWith(packageUsage.PackageName, StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            if (packageNamespacesInFile.Any())
                            {
                                fileHasUsage = true;
                                foreach (var ns in packageNamespacesInFile)
                                {
                                    packageUsage.UsedNamespaces.Add(ns);
                                    if (!packageUsage.TypesByNamespace.ContainsKey(ns))
                                        packageUsage.TypesByNamespace[ns] = new HashSet<string>();
                                }

                                ExtractTypesWithSemanticModel(root, semanticModel, packageNamespacesInFile, packageUsage);
                                packageUsage.Files.Add(Path.GetFileName(tree.FilePath));
                            }
                        }
                        if (fileHasUsage) filesWithUsage++;
                    }
                    catch
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

        public static void ExtractTypesWithSemanticModel(
            SyntaxNode root, SemanticModel semanticModel, List<string> packageNamespaces, PackageUsageDetail packageUsage)
        {
            // Identifiers
            foreach (var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                TryResolveTypeSymbol(identifier, semanticModel, packageNamespaces, packageUsage);
            }

            // Generic names
            foreach (var genericName in root.DescendantNodes().OfType<GenericNameSyntax>())
            {
                TryResolveTypeSymbol(genericName, semanticModel, packageNamespaces, packageUsage);
            }

            // Base types
            foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (typeDecl.BaseList == null) continue;

                foreach (var baseType in typeDecl.BaseList.Types)
                {
                    try
                    {
                        var typeInfo = semanticModel.GetTypeInfo(baseType.Type);
                        if (typeInfo.Type is INamedTypeSymbol symbol)
                        {
                            TryAddTypeFromSymbol(symbol, packageNamespaces, packageUsage);
                        }
                    }
                    catch { }
                }
            }

            AnalyzeMigrationCompatibility(root, semanticModel, packageUsage);
        }

        private static void TryResolveTypeSymbol(
            SyntaxNode node, SemanticModel semanticModel, List<string> packageNamespaces, PackageUsageDetail packageUsage)
        {
            try
            {
                var symbolInfo = semanticModel.GetSymbolInfo(node);
                if (symbolInfo.Symbol is INamedTypeSymbol typeSymbol)
                {
                    TryAddTypeFromSymbol(typeSymbol, packageNamespaces, packageUsage);
                }
            }
            catch { }
        }

        private static void TryAddTypeFromSymbol(
            INamedTypeSymbol typeSymbol, List<string> packageNamespaces, PackageUsageDetail packageUsage)
        {
            var containingNamespace = typeSymbol.ContainingNamespace?.ToDisplayString();
            if (string.IsNullOrEmpty(containingNamespace)) return;

            foreach (var ns in packageNamespaces)
            {
                if (containingNamespace.StartsWith(ns, StringComparison.OrdinalIgnoreCase))
                {
                    var typeName = typeSymbol.Name;
                    if (packageUsage.TypesByNamespace.ContainsKey(ns))
                        packageUsage.TypesByNamespace[ns].Add(typeName);
                    packageUsage.UsedTypes.Add(typeName);

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

        private static void AnalyzeMigrationCompatibility(
            SyntaxNode root, SemanticModel semanticModel, PackageUsageDetail packageUsage)
        {
            var frameworkSpecificNamespaces = FrameworkCompatibility.GetFrameworkSpecificNamespaces();
            var compilation = semanticModel.Compilation;

            var packageAssemblyInfo = AnalyzePackageAssembly(packageUsage.PackageName, compilation);
            packageUsage.PackageTargetFramework = packageAssemblyInfo.TargetFramework;
            packageUsage.PackageAssemblyDependencies = packageAssemblyInfo.Dependencies;

            foreach (var typeDetail in packageUsage.TypeDetails.Values)
            {
                var dependencies = new HashSet<string>();

                foreach (var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    try
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(identifier);
                        var symbol = symbolInfo.Symbol;
                        if (symbol == null) continue;

                        var containingNamespace = symbol.ContainingNamespace?.ToDisplayString();
                        var assemblyName = symbol.ContainingAssembly?.Name;

                        if (!string.IsNullOrEmpty(containingNamespace) && !string.IsNullOrEmpty(assemblyName))
                        {
                            foreach (var fwNs in frameworkSpecificNamespaces)
                            {
                                if (containingNamespace.StartsWith(fwNs.Key, StringComparison.OrdinalIgnoreCase))
                                {
                                    dependencies.Add($"{containingNamespace} ({fwNs.Value})");
                                    break;
                                }
                            }

                            var isFrameworkOnlyAssembly = FrameworkCompatibility.IsFrameworkOnlyAssembly(assemblyName);
                            if (isFrameworkOnlyAssembly != null)
                            {
                                dependencies.Add($"{assemblyName} ({isFrameworkOnlyAssembly})");
                            }
                        }
                    }
                    catch { }
                }

                typeDetail.FrameworkDependencies = dependencies.ToList();
                typeDetail.IsNetStandardCompatible = !dependencies.Any() &&
                    !packageUsage.PackageAssemblyDependencies.Any(d => d.IsFrameworkOnly);
            }
        }

        private static AssemblyAnalysisInfo AnalyzePackageAssembly(string packageName, Compilation compilation)
        {
            var info = new AssemblyAnalysisInfo { PackageName = packageName };

            try
            {
                var packageAssembly = compilation.References
                    .OfType<PortableExecutableReference>()
                    .Select(r => compilation.GetAssemblyOrModuleSymbol(r) as IAssemblySymbol)
                    .FirstOrDefault(a => a != null && a.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));

                if (packageAssembly != null)
                {
                    var targetFrameworkAttr = packageAssembly.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.Name == "TargetFrameworkAttribute");

                    if (targetFrameworkAttr != null && targetFrameworkAttr.ConstructorArguments.Length > 0)
                    {
                        info.TargetFramework = targetFrameworkAttr.ConstructorArguments[0].Value?.ToString() ?? "Unknown";
                    }

                    foreach (var module in packageAssembly.Modules)
                    {
                        foreach (var referencedAssembly in module.ReferencedAssemblies)
                        {
                            var depName = referencedAssembly.Name;
                            var isFrameworkOnly = FrameworkCompatibility.IsFrameworkOnlyAssembly(depName);

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
            catch { }

            return info;
        }
    }
}
