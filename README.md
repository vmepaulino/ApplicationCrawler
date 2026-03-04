# DotNet Crawler

A .NET Framework 4.8.1 application that analyzes NuGet package usage across .NET solutions. This tool helps you understand which NuGet packages are referenced in your projects and, more importantly, which types and namespaces from those packages are actually being used in your codebase.

## Features

- ? **Solution-wide Analysis** - Scans all projects in a .NET solution
- ? **Wildcard Pattern Matching** - Filter packages using patterns like `Newtonsoft.*`
- ? **Code Usage Detection** - Identifies which namespaces and types are actually used
- ? **Multiple Project Format Support** - Works with both SDK-style projects and packages.config
- ? **HTML Report Generation** - Creates beautiful, interactive HTML reports
- ? **Unused Package Detection** - Highlights packages that are referenced but never used
- ? **.NET Framework 4.7.2+ Compatible** - Works with legacy .NET Framework projects

## Usage

### Basic Usage

```cmd
DotNetCrawler.exe --solution MySolution.sln --dependencies "PackageName.*"
```

### With HTML Report

```cmd
DotNetCrawler.exe -s MySolution.sln -d "Newtonsoft.*" "System.*" -o report.html
```

### Command Line Options

- `-s, --solution <path>` - Path to the solution file (.sln) **[Required]**
- `-d, --dependencies <patterns>` - NuGet package name patterns (supports wildcards) **[Required]**
- `-o, --output <file>` - Path to output HTML report **[Optional]**

## Examples

### Analyze Newtonsoft.Json usage
```cmd
DotNetCrawler.exe -s "C:\Projects\MyApp.sln" -d "Newtonsoft.*"
```

### Analyze multiple package families
```cmd
DotNetCrawler.exe -s MySolution.sln -d "System.Net.*" "Microsoft.Extensions.*" "Newtonsoft.*"
```

### Generate HTML report
```cmd
DotNetCrawler.exe -s MySolution.sln -d "MyCompany.*" -o "usage-report.html"
```

## HTML Report Features

The generated HTML report includes:

- ?? **Summary Dashboard** - Overview of projects, packages, types, and usage statistics
- ?? **Project Breakdown** - Each project with its referenced packages
- ?? **Package Details** - Version information and usage metrics
- ?? **Namespace Tracking** - Exact namespaces used from each package
- ?? **Type Tracking** - Specific types/classes used from each package
- ?? **File References** - List of files that use each package
- ?? **Unused Package Warnings** - Highlights packages that aren't actually used
- ?? **Interactive UI** - Expandable/collapsible sections for easy navigation

## Output Example

### Console Output
```
?? Analyzing solution: MySolution.sln
?? Dependency patterns: Newtonsoft.*

? Loading solution...
? Found 5 projects

?? Project: WebApp
   ? Found: Newtonsoft.Json (v13.0.1)

?? SUMMARY
============================================================
Found 1 matching packages:

  ?? Newtonsoft.Json
     Used in 3 project(s): WebApp, API, Services

?? CODE USAGE ANALYSIS
============================================================

?? Project: WebApp

  ?? Newtonsoft.Json (v13.0.1)
     Namespaces: 2
     Types: 15
     Files: 5
     Top Namespaces:
        - Newtonsoft.Json
        - Newtonsoft.Json.Linq
     Top Types Used:
        - JsonConvert
        - JObject
        - JArray
        - JsonSerializer
        - JsonProperty
        ... and 10 more

?? HTML report generated: C:\Projects\usage-report.html

? Analysis complete!
```

## How It Works

1. **Solution Parsing** - Reads the .sln file to identify all C# projects
2. **Package Discovery** - Extracts NuGet packages from both PackageReference and packages.config
3. **Pattern Matching** - Filters packages based on provided wildcard patterns
4. **Code Analysis** - Uses Roslyn to parse C# files and detect using directives
5. **Usage Tracking** - Maps namespaces to packages and tracks file-level usage
6. **Report Generation** - Creates console output and optional HTML report

## Technical Details

- **Target Framework**: .NET Framework 4.8.1
- **C# Version**: 7.3
- **Key Dependencies**:
  - Microsoft.CodeAnalysis.CSharp (4.0.1) - For syntax parsing
  - System.CommandLine (2.0.0-beta4) - For command-line argument parsing

## Building the Project

Open the solution in Visual Studio and build:

```cmd
msbuild DotNetCrawler.sln /t:Restore,Build /p:Configuration=Release
```

Or in Visual Studio:
1. Open `ApplicationCrawler.sln`
2. Right-click solution ? Restore NuGet Packages
3. Build ? Build Solution (Ctrl+Shift+B)

## License

This project is open source and available for use in analyzing .NET projects.

## Contributing

Contributions are welcome! Areas for enhancement:
- Export to CSV/JSON formats
- Assembly reference analysis (not just NuGet packages)
- Method-level usage tracking
- Dependency graph visualization
- Integration with CI/CD pipelines

## Use Cases

- **Package Cleanup** - Identify and remove unused NuGet packages
- **Dependency Auditing** - Understand which parts of a library you're actually using
- **Migration Planning** - Assess usage before upgrading or replacing packages
- **License Compliance** - Know exactly which package features you're using
- **Technical Debt Analysis** - Find packages that are referenced but never used
