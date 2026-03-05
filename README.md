# Application Crawler

A monorepo containing tools for static analysis and code health evaluation across .NET and Angular projects.

---

## DotNet Crawler

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

---

## Angular App Analyser

A .NET 10 console application that performs deep static analysis, security scanning, and code quality evaluation on Angular projects. Produces both console output and a rich interactive HTML report.

### Features

- ??? **Security Scanning** — XSS vectors, hardcoded secrets, CSP, innerHTML, `eval()`, insecure URLs
- ?? **Storage Analysis** — localStorage/sessionStorage usage, auth token storage patterns
- ?? **API Communication** — Interceptor detection, CSRF, hardcoded URLs, endpoint extraction
- ??? **Application Design** — Lazy loading, OnPush, subscription cleanup, DOM access, route guards
- ?? **App Structure** — Per-folder recursive analysis of components, services, directives, pipes, guards
- ?? **Library Health** — Deprecated packages, version checks, Angular version EOL warnings
- ?? **Library Versions** — Runs `npm outdated --json` to detect outdated dependencies
- ?? **Security Posture** — Scorecard of 15+ security controls (auth, CSRF, CSP, strict mode, etc.)
- ?? **npm audit** — Runs `npm audit --json` to find known CVEs in dependencies
- ?? **ESLint / ng lint** — Runs linter for Angular best practices and code quality
- ?? **TypeScript Type Check** — Runs `tsc --noEmit` to find compilation errors
- ? **Component Modernization Score** — Rates each component 1–5 on modern Angular traits (signals, standalone, OnPush, `@if`/`@for`, etc.)

### Architecture

The tool uses a **Strategy + Pipeline** pattern. Each analysis area is a self-contained step implementing `IAnalysisStep`. Report output is decoupled via `IReportWriter`.

```
AngularAppAnalyser/
??? Program.cs                          — Slim entry point
??? Abstractions/
?   ??? IAnalysisStep.cs                — Contract: implement to add new analysis
?   ??? IReportWriter.cs                — Contract: implement for new output formats
?   ??? AnalysisContext.cs              — Shared state + utility methods
??? Models/
?   ??? Models.cs                       — Pure data classes
??? Analyzers/
?   ??? AnalysisSteps.cs                — IAnalysisStep wrappers (one per area)
?   ??? ProjectMetadataStep.cs          — package.json / angular.json parsing
?   ??? SecurityStep.cs                 — XSS, secrets, CSP patterns
?   ??? StorageStep.cs                  — localStorage, cookies
?   ??? ApiCommunicationAnalyzer.cs     — HTTP patterns + endpoint extraction
?   ??? DesignAnalyzer.cs               — Lazy loading, guards, OnPush
?   ??? AppStructureAnalyzer.cs         — Areas, services, component scoring
?   ??? LibraryAnalyzer.cs              — Deprecated libs + npm outdated
?   ??? SecurityPostureAnalyzer.cs      — Posture scorecard
?   ??? CliRunner.cs                    — Shared CLI process runner
?   ??? NpmAuditAnalyzer.cs             — npm audit JSON parser
?   ??? LinterAnalyzer.cs              — ESLint / ng lint JSON parser
?   ??? TypeCheckAnalyzer.cs            — tsc --noEmit output parser
??? Reporting/
?   ??? ConsoleReportWriter.cs          — Console summary
?   ??? HtmlReportWriter.cs             — Full interactive HTML report
??? Engine/
    ??? AnalysisEngine.cs               — Orchestrates steps + writers
```

**Adding a new analysis area** = 1 new class implementing `IAnalysisStep` + register in `Program.cs`.

### Usage

```cmd
cd AngularAppAnalyser
dotnet run -- --path <angular-app-folder> [--output <report.html>]
```

#### Options

| Flag | Description |
|------|-------------|
| `-p, --path <path>` | Path to the Angular app root (containing `package.json`) **[Required]** |
| `-o, --output <file>` | Path to output HTML report **[Optional]** |

#### Examples

```cmd
# Console-only analysis
dotnet run -- -p ../my-angular-app

# With HTML report
dotnet run -- --path C:\Projects\MyApp --output report.html
```

### Prerequisites

- .NET 10 SDK
- Node.js / npm (for `npm outdated`, `npm audit`, `tsc --noEmit`)
- ESLint configured in the Angular project for lint step (`ng add @angular-eslint/schematics`)

### Console Output Example

```
============================================================
?? Angular App Analyser
   Path: C:\Projects\MyApp
============================================================

[Step  1/12] ?? Parsing project metadata...
[Step  1/12] ? Angular v18.2.0, 22 deps, 15 devDeps (45ms)

[Step  5/12] ??? Analyzing application design & browser resources...
[Step  5/12] ? 12 design finding(s) (120ms)

[Step  6/12] ?? Analyzing app structure, services & components...
   ?? regulatory: 14 .ts, 10 .html, 6 comp (avg 3.2/5), 2 svc, 0 guard, 1 mod
      ? sub-folders: countries, jurisdictional-standards
[Step  6/12] ? 8 area(s), 42 .ts, 30 .html, 18 component(s) avg 3.1/5, 6 service(s), 12 issue(s) (85ms)

[Step 10/12] ?? Running npm audit (dependency vulnerabilities)...
[Step 10/12] ? 5 vulnerable package(s) — 1 critical, 2 high (3200ms)

[Step 11/12] ?? Running ESLint / ng lint (code quality)...
[Step 11/12] ? 45 issue(s) in 12 file(s) — 8 errors, 37 warnings (ng lint (ESLint)) (5400ms)

[Step 12/12] ?? Running TypeScript type check (tsc --noEmit)...
[Step 12/12] ? clean — no type errors (2100ms)

??  Total elapsed time: 12340ms
? Analysis complete!
```

### Technical Details

- **Target Framework**: .NET 10
- **C# Version**: 14.0
- **External Tools Used**: `npm outdated`, `npm audit`, `npx ng lint`, `npx tsc`
- **No NuGet dependencies** — uses only `System.Text.Json` and `System.Text.RegularExpressions`
