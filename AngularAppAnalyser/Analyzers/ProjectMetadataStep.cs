using System.Text.Json;
using AngularAppAnalyser.Abstractions;
using AngularAppAnalyser.Models;

namespace AngularAppAnalyser.Analyzers;

public sealed class ProjectMetadataStep : IAnalysisStep
{
    public string Name => "Parsing project metadata";
    public string Icon => "\ud83d\udce6";
    public int Order => 10;

    public void Execute(AnalysisContext context)
    {
        var meta = new ProjectMetadata();
        var appPath = context.AppPath;
        var packageJsonPath = Path.Combine(appPath, "package.json");

        try
        {
            var json = File.ReadAllText(packageJsonPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            meta.Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            meta.Version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";

            if (root.TryGetProperty("dependencies", out var deps))
                foreach (var prop in deps.EnumerateObject())
                    meta.Dependencies[prop.Name] = prop.Value.GetString() ?? "";

            if (root.TryGetProperty("devDependencies", out var devDeps))
                foreach (var prop in devDeps.EnumerateObject())
                    meta.DevDependencies[prop.Name] = prop.Value.GetString() ?? "";

            if (meta.Dependencies.TryGetValue("@angular/core", out var angularVersion))
                meta.AngularVersion = angularVersion.TrimStart('^', '~');
            else if (meta.DevDependencies.TryGetValue("@angular/core", out angularVersion))
                meta.AngularVersion = angularVersion.TrimStart('^', '~');
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   \u26a0\ufe0f  Error parsing package.json: {ex.Message}");
        }

        var angularJsonPath = Path.Combine(appPath, "angular.json");
        if (File.Exists(angularJsonPath))
        {
            try
            {
                var json = File.ReadAllText(angularJsonPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("projects", out var projects))
                {
                    foreach (var proj in projects.EnumerateObject())
                    {
                        meta.AngularProjects.Add(proj.Name);
                        Console.WriteLine($"   \ud83d\udcc1 Angular project: {proj.Name}");

                        if (proj.Value.TryGetProperty("architect", out var architect) &&
                            architect.TryGetProperty("build", out var build) &&
                            build.TryGetProperty("configurations", out var configs) &&
                            configs.TryGetProperty("production", out var prod) &&
                            prod.TryGetProperty("budgets", out _))
                        {
                            meta.HasBudgets = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   \u26a0\ufe0f  Error parsing angular.json: {ex.Message}");
            }
        }

        meta.HasTsConfig = File.Exists(Path.Combine(appPath, "tsconfig.json"));
        meta.HasTsConfigApp = File.Exists(Path.Combine(appPath, "tsconfig.app.json"));

        context.Report.ProjectMetadata = meta;
    }

    public string GetSummary(AnalysisContext context)
    {
        var m = context.Report.ProjectMetadata;
        return $"Angular v{m.AngularVersion ?? "unknown"}, {m.Dependencies.Count} deps, {m.DevDependencies.Count} devDeps";
    }
}
