using System.CommandLine;
using System.Text.RegularExpressions;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

var solutionOption = new Option<FileInfo>(
    aliases: ["--solution", "-s"],
    description: "Path to the solution file (.sln)")
{
    IsRequired = true
};

var dependenciesOption = new Option<string[]>(
    aliases: ["--dependencies", "-d"],
    description: "NuGet package name patterns to analyze (supports wildcards like LibName.*)")
{
    IsRequired = true,
    AllowMultipleArgumentsPerToken = true
};

var rootCommand = new RootCommand("Application Crawler - Analyzes NuGet package usage in .NET solutions");
rootCommand.AddOption(solutionOption);
rootCommand.AddOption(dependenciesOption);

rootCommand.SetHandler(async (FileInfo solution, string[] dependencies) =>
{
    await AnalyzeSolutionAsync(solution, dependencies);
}, solutionOption, dependenciesOption);

return await rootCommand.InvokeAsync(args);

static async Task AnalyzeSolutionAsync(FileInfo solutionFile, string[] dependencyPatterns)
{
    if (!solutionFile.Exists)
    {
        Console.Error.WriteLine($"Error: Solution file not found: {solutionFile.FullName}");
        return;
    }

    Console.WriteLine($"🔍 Analyzing solution: {solutionFile.Name}");
    Console.WriteLine($"📦 Dependency patterns: {string.Join(", ", dependencyPatterns)}");
    Console.WriteLine();

    // Register MSBuild defaults
    MSBuildLocator.RegisterDefaults();

    using var workspace = MSBuildWorkspace.Create();
    
    // Suppress build warnings
    workspace.WorkspaceFailed += (sender, e) =>
    {
        if (e.Diagnostic.Kind != WorkspaceDiagnosticKind.Failure)
        {
            // Only log failures, skip warnings for cleaner output
        }
    };

    Console.WriteLine("⏳ Loading solution...");
    var solution = await workspace.OpenSolutionAsync(solutionFile.FullName);
    
    Console.WriteLine($"✅ Loaded {solution.Projects.Count()} projects");
    Console.WriteLine();

    var patterns = dependencyPatterns.Select(ConvertWildcardToRegex).ToArray();
    var matchedPackages = new Dictionary<string, HashSet<string>>(); // Package -> Projects

    foreach (var project in solution.Projects)
    {
        Console.WriteLine($"📁 Project: {project.Name}");
        
        var packages = GetNuGetPackages(project);
        
        foreach (var package in packages)
        {
            if (patterns.Any(p => p.IsMatch(package.name)))
            {
                Console.WriteLine($"   ✓ Found: {package.name} (v{package.version})");
                
                if (!matchedPackages.ContainsKey(package.name))
                {
                    matchedPackages[package.name] = new HashSet<string>();
                }
                matchedPackages[package.name].Add(project.Name);
            }
        }
        
        if (!packages.Any(p => patterns.Any(pat => pat.IsMatch(p.name))))
        {
            Console.WriteLine($"   - No matching packages");
        }
        Console.WriteLine();
    }

    Console.WriteLine("=" .Repeat(60));
    Console.WriteLine("📊 SUMMARY");
    Console.WriteLine("=" .Repeat(60));
    
    if (matchedPackages.Any())
    {
        Console.WriteLine($"\nFound {matchedPackages.Count} matching packages:");
        foreach (var kvp in matchedPackages.OrderBy(x => x.Key))
        {
            Console.WriteLine($"\n  📦 {kvp.Key}");
            Console.WriteLine($"     Used in {kvp.Value.Count} project(s): {string.Join(", ", kvp.Value)}");
        }

        Console.WriteLine("\n🔄 Next Step: Analyzing actual code usage...");
        await AnalyzeCodeUsageAsync(solution, matchedPackages.Keys.ToArray());
    }
    else
    {
        Console.WriteLine("\n⚠️  No matching packages found.");
    }
}

static List<(string name, string version)> GetNuGetPackages(Project project)
{
    var packages = new List<(string name, string version)>();
    
    // Get package references from project file
    var projectFilePath = project.FilePath;
    if (string.IsNullOrEmpty(projectFilePath) || !File.Exists(projectFilePath))
        return packages;

    try
    {
        var projectXml = System.Xml.Linq.XDocument.Load(projectFilePath);
        var packageRefs = projectXml.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => new
            {
                Name = e.Attribute("Include")?.Value,
                Version = e.Attribute("Version")?.Value ?? e.Elements().FirstOrDefault(el => el.Name.LocalName == "Version")?.Value
            })
            .Where(p => !string.IsNullOrEmpty(p.Name));

        packages.AddRange(packageRefs.Select(p => (p.Name!, p.Version ?? "unknown")));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   ⚠️  Warning: Could not read packages from {project.Name}: {ex.Message}");
    }

    return packages;
}

static Regex ConvertWildcardToRegex(string pattern)
{
    var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
    return new Regex(regexPattern, RegexOptions.IgnoreCase);
}

static async Task AnalyzeCodeUsageAsync(Solution solution, string[] packageNames)
{
    Console.WriteLine("\n🔬 Analyzing code usage (this may take a moment)...");
    
    var usageReport = new Dictionary<string, PackageUsageInfo>();
    
    foreach (var packageName in packageNames)
    {
        usageReport[packageName] = new PackageUsageInfo { PackageName = packageName };
    }

    foreach (var project in solution.Projects)
    {
        var compilation = await project.GetCompilationAsync();
        if (compilation == null) continue;

        foreach (var document in project.Documents)
        {
            if (document.FilePath?.EndsWith(".cs") != true) continue;

            var semanticModel = await document.GetSemanticModelAsync();
            if (semanticModel == null) continue;

            var root = await document.GetSyntaxRootAsync();
            if (root == null) continue;

            // Get all identifiers (types, methods, properties, etc.)
            var nodes = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax>();

            foreach (var node in nodes)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(node);
                var symbol = symbolInfo.Symbol;

                if (symbol == null) continue;

                var containingAssembly = symbol.ContainingAssembly?.Name;
                if (string.IsNullOrEmpty(containingAssembly)) continue;

                // Check if this symbol belongs to one of our target packages
                foreach (var packageName in packageNames)
                {
                    // Simple heuristic: assembly name often matches package name
                    if (containingAssembly.Equals(packageName, StringComparison.OrdinalIgnoreCase) ||
                        containingAssembly.StartsWith(packageName + ".", StringComparison.OrdinalIgnoreCase))
                    {
                        var info = usageReport[packageName];
                        info.UsageCount++;
                        info.UsedTypes.Add($"{symbol.ContainingNamespace}.{symbol.ContainingType?.Name ?? symbol.Name}");
                        info.Files.Add(document.FilePath!);
                        break;
                    }
                }
            }
        }
    }

    Console.WriteLine("\n" + "=".Repeat(60));
    Console.WriteLine("📈 CODE USAGE ANALYSIS");
    Console.WriteLine("=".Repeat(60));

    foreach (var kvp in usageReport.OrderByDescending(x => x.Value.UsageCount))
    {
        var info = kvp.Value;
        Console.WriteLine($"\n📦 {info.PackageName}");
        Console.WriteLine($"   References: {info.UsageCount}");
        Console.WriteLine($"   Unique Types: {info.UsedTypes.Count}");
        Console.WriteLine($"   Files: {info.Files.Count}");
        
        if (info.UsedTypes.Any())
        {
            Console.WriteLine($"   Top Types Used:");
            foreach (var type in info.UsedTypes.Take(5))
            {
                Console.WriteLine($"      - {type}");
            }
            if (info.UsedTypes.Count > 5)
            {
                Console.WriteLine($"      ... and {info.UsedTypes.Count - 5} more");
            }
        }
        else
        {
            Console.WriteLine($"   ⚠️  Package referenced but not used in code!");
        }
    }
}

class PackageUsageInfo
{
    public string PackageName { get; set; } = "";
    public int UsageCount { get; set; }
    public HashSet<string> UsedTypes { get; set; } = new();
    public HashSet<string> Files { get; set; } = new();
}

static class StringExtensions
{
    public static string Repeat(this string s, int count) => string.Concat(Enumerable.Repeat(s, count));
}
