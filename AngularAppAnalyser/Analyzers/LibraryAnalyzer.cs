using System.Text.Json;
using AngularAppAnalyser.Models;

namespace AngularAppAnalyser.Analyzers;

internal static class LibraryAnalyzer
{
    public static List<Finding> Analyze(ProjectMetadata metadata)
    {
        var findings = new List<Finding>();

        if (metadata.AngularVersion is not null)
        {
            var versionParts = metadata.AngularVersion.Split('.');
            if (versionParts.Length > 0 && int.TryParse(versionParts[0], out var majorVersion) && majorVersion > 0)
            {
                if (majorVersion < 16)
                    findings.Add(new Finding { Title = $"Angular {majorVersion} is end-of-life", Description = $"Angular {metadata.AngularVersion} is no longer receiving security patches. Upgrade to Angular 16+ (18+ ideal).", Severity = Severity.Critical, Category = "Libraries" });
                else if (majorVersion < 18)
                    findings.Add(new Finding { Title = $"Angular {majorVersion} approaching end-of-support", Description = $"Angular {metadata.AngularVersion} is in LTS. Plan an upgrade to Angular 18+ for signals, control flow, etc.", Severity = Severity.Medium, Category = "Libraries" });
            }
        }

        var knownIssues = new Dictionary<string, (string message, Severity severity)>(StringComparer.OrdinalIgnoreCase)
        {
            ["protractor"] = ("Protractor is deprecated. Migrate to Cypress, Playwright, or Angular's built-in e2e.", Severity.High),
            ["tslint"] = ("TSLint is deprecated since 2019. Migrate to ESLint with @angular-eslint.", Severity.High),
            ["codelyzer"] = ("Codelyzer is deprecated. Use @angular-eslint.", Severity.Medium),
            ["@angular/http"] = ("@angular/http is removed. Use @angular/common/http (HttpClient).", Severity.Critical),
            ["rxjs-compat"] = ("rxjs-compat is a migration shim. Remove it and update to RxJS 6+ syntax.", Severity.Medium),
            ["node-sass"] = ("node-sass is deprecated. Switch to sass (Dart Sass).", Severity.High),
            ["@ngrx/store-devtools"] = ("Should only be in devDependencies, not dependencies.", Severity.Medium),
            ["jquery"] = ("jQuery in Angular is an anti-pattern. Use Angular's built-in DOM handling.", Severity.Medium),
            ["moment"] = ("Moment.js is maintenance-only and adds ~300KB. Use date-fns, Luxon, or native Date/Intl.", Severity.Medium),
            ["lodash"] = ("Consider lodash-es for tree-shaking, or use native JS methods.", Severity.Low),
            ["angular-in-memory-web-api"] = ("Should only be in devDependencies.", Severity.Medium),
            ["@angular/flex-layout"] = ("Deprecated. Migrate to CSS Flexbox/Grid or TailwindCSS.", Severity.High),
            ["angularfire2"] = ("Renamed to @angular/fire. Update the package name.", Severity.Medium),
            ["core-js"] = ("May be unnecessary for modern browsers. Angular CLI handles polyfills.", Severity.Low),
            ["classlist.js"] = ("No longer needed for modern browsers.", Severity.Low),
            ["web-animations-js"] = ("No longer needed. Modern browsers support Web Animations API.", Severity.Low),
        };

        foreach (var dep in metadata.Dependencies.Concat(metadata.DevDependencies))
        {
            if (knownIssues.TryGetValue(dep.Key, out var issue) && !string.IsNullOrEmpty(issue.message))
                findings.Add(new Finding { Title = $"{dep.Key} ({dep.Value})", Description = issue.message, Severity = issue.severity, Category = "Libraries" });
        }

        if (!metadata.DevDependencies.ContainsKey("eslint") && !metadata.DevDependencies.ContainsKey("@angular-eslint/builder"))
            findings.Add(new Finding { Title = "No linter configured", Description = "Neither ESLint nor @angular-eslint found.", Severity = Severity.Medium, Category = "Libraries" });

        if (metadata.DevDependencies.TryGetValue("typescript", out var tsVersion))
        {
            var clean = tsVersion.TrimStart('^', '~').Split('.');
            if (clean.Length > 0 && int.TryParse(clean[0], out var tsMajor) && tsMajor < 5)
                findings.Add(new Finding { Title = $"TypeScript {tsVersion.TrimStart('^', '~')} is outdated", Description = "TypeScript 5.x has significant improvements. Upgrade alongside Angular.", Severity = Severity.Medium, Category = "Libraries" });
        }

        if (metadata.Dependencies.TryGetValue("rxjs", out var rxjsVersion))
        {
            var clean = rxjsVersion.TrimStart('^', '~').Split('.');
            if (clean.Length > 0 && int.TryParse(clean[0], out var rxMajor) && rxMajor < 7)
                findings.Add(new Finding { Title = $"RxJS {rxjsVersion.TrimStart('^', '~')} is outdated", Description = "RxJS 7+ has smaller bundle and better types.", Severity = Severity.Medium, Category = "Libraries" });
        }

        return findings;
    }

    public static List<LibraryVersionInfo> CheckVersions(string appPath)
    {
        var versions = new List<LibraryVersionInfo>();

        try
        {
            var (output, stderr, exitCode) = CliRunner.Run(appPath, "npm", "outdated --json", 30_000, progressLabel: "npm outdated");

            if (!string.IsNullOrWhiteSpace(output))
            {
                using var doc = JsonDocument.Parse(output);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var info = new LibraryVersionInfo { Name = prop.Name };
                    if (prop.Value.TryGetProperty("current", out var current)) info.Current = current.GetString() ?? "";
                    if (prop.Value.TryGetProperty("wanted", out var wanted)) info.Wanted = wanted.GetString() ?? "";
                    if (prop.Value.TryGetProperty("latest", out var latest)) info.Latest = latest.GetString() ?? "";
                    info.UpdateType = DetermineUpdateType(info.Current, info.Latest);
                    versions.Add(info);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   \u26a0\ufe0f  Could not run npm outdated: {ex.Message}");
        }

        return versions;
    }

    public static string DetermineUpdateType(string current, string latest)
    {
        if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(latest)) return "unknown";
        var curParts = current.TrimStart('^', '~').Split('.');
        var latParts = latest.TrimStart('^', '~').Split('.');
        if (curParts.Length > 0 && latParts.Length > 0 && curParts[0] != latParts[0]) return "major";
        if (curParts.Length > 1 && latParts.Length > 1 && curParts[1] != latParts[1]) return "minor";
        if (curParts.Length > 2 && latParts.Length > 2 && curParts[2] != latParts[2]) return "patch";
        return "current";
    }
}
