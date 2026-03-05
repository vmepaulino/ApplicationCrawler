using System.Text.Json;
using AngularAppAnalyser.Models;

namespace AngularAppAnalyser.Analyzers;

/// <summary>
/// Runs <c>npm audit --json</c> to find known vulnerabilities in dependencies.
/// </summary>
internal static class NpmAuditAnalyzer
{
    public static NpmAuditResult Run(string appPath)
    {
        var result = new NpmAuditResult();

        if (!File.Exists(Path.Combine(appPath, "package-lock.json")) &&
            !File.Exists(Path.Combine(appPath, "yarn.lock")) &&
            !File.Exists(Path.Combine(appPath, "pnpm-lock.yaml")))
        {
            result.Error = "No lock file found — npm audit requires package-lock.json";
            return result;
        }

        var (stdout, stderr, exitCode) = CliRunner.Run(appPath, "npm", "audit --json", 60_000, progressLabel: "npm audit");

        if (string.IsNullOrWhiteSpace(stdout))
        {
            result.Error = string.IsNullOrWhiteSpace(stderr) ? "npm audit produced no output" : stderr;
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            result.WasRun = true;

            // npm audit v2+ format: { "vulnerabilities": { "package-name": { ... } } }
            if (root.TryGetProperty("vulnerabilities", out var vulns))
            {
                foreach (var prop in vulns.EnumerateObject())
                {
                    var v = new NpmAuditVulnerability { Name = prop.Name };

                    if (prop.Value.TryGetProperty("severity", out var sev))
                        v.Severity = sev.GetString() ?? "info";
                    if (prop.Value.TryGetProperty("range", out var range))
                        v.Range = range.GetString() ?? "";
                    if (prop.Value.TryGetProperty("isDirect", out var isDirect))
                        v.IsDirect = isDirect.GetBoolean();

                    // "via" can be array of strings or objects
                    if (prop.Value.TryGetProperty("via", out var via) && via.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in via.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                v.Via.Add(item.GetString() ?? "");
                            }
                            else if (item.ValueKind == JsonValueKind.Object)
                            {
                                if (item.TryGetProperty("title", out var title))
                                    v.Title = title.GetString() ?? "";
                                if (item.TryGetProperty("url", out var url))
                                    v.Url = url.GetString() ?? "";
                                if (item.TryGetProperty("name", out var name))
                                    v.Via.Add(name.GetString() ?? "");
                            }
                        }
                    }

                    result.Vulnerabilities.Add(v);
                }
            }

            // Summary metadata
            if (root.TryGetProperty("metadata", out var meta) &&
                meta.TryGetProperty("vulnerabilities", out var summary))
            {
                result.TotalVulnerabilities = 0;
                foreach (var s in summary.EnumerateObject())
                {
                    if (s.Value.ValueKind == JsonValueKind.Number)
                        result.TotalVulnerabilities += s.Value.GetInt32();
                }
            }
            else
            {
                result.TotalVulnerabilities = result.Vulnerabilities.Count;
            }
        }
        catch (Exception ex)
        {
            result.Error = $"Failed to parse npm audit output: {ex.Message}";
        }

        return result;
    }
}
