using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DotNetCrawler.Models;

namespace DotNetCrawler.Reporting
{
    /// <summary>
    /// Generates the interactive HTML report for dependency analysis.
    /// Delegates to private methods for each report section.
    /// </summary>
    internal static class HtmlReportWriter
    {
        public static void Generate(string solutionPath, List<ProjectAnalysisResult> results,
            List<DllManifestAnalysis> manifestResults, string outputFile)
        {
            var html = new StringBuilder();
            var solutionName = Path.GetFileNameWithoutExtension(solutionPath);

            WriteHead(html, solutionName);
            WriteSummary(html, solutionName, results);
            WriteProjectSections(html, results);
            WriteMigrationSection(html, results);
            WriteManifestSection(html, manifestResults);
            WriteTypeIndexSection(html, results);
            WriteScriptsAndClose(html);

            File.WriteAllText(outputFile, html.ToString());
        }

        // ?? Head + CSS ??????????????????????????????????????????

        private static void WriteHead(StringBuilder html, string solutionName)
        {
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
        }

        // ?? Summary ?????????????????????????????????????????????

        private static void WriteSummary(StringBuilder html, string solutionName, List<ProjectAnalysisResult> results)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            html.AppendLine($"        <h1>\ud83d\udcca NuGet Usage Report: {solutionName}</h1>");
            html.AppendLine($"        <div class='header-info'>Generated on {timestamp}</div>");

            var totalProjects = results.Count;
            var totalPackages = results.SelectMany(r => r.PackageUsages).Select(p => p.PackageName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
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
        }

        // ?? Per-project package usage ???????????????????????????

        private static void WriteProjectSections(StringBuilder html, List<ProjectAnalysisResult> results)
        {
            foreach (var project in results.OrderBy(p => p.ProjectName))
            {
                if (!project.PackageUsages.Any()) continue;

                html.AppendLine("        <div class='project'>");
                html.AppendLine($"            <div class='project-header' onclick='toggleProject(\"{project.ProjectName}\")'>");
                html.AppendLine($"                <div><h2>\ud83d\udcc1 {project.ProjectName}</h2>");
                html.AppendLine($"                <div class='project-stats'>{project.PackageUsages.Count} package(s) referenced</div></div>");
                html.AppendLine($"                <span class='toggle-icon' id='icon-{project.ProjectName}'>\u25b6</span>");
                html.AppendLine("            </div>");
                html.AppendLine($"            <div class='project-content' id='content-{project.ProjectName}' style='display:none;'>");

                foreach (var package in project.PackageUsages.OrderBy(p => p.PackageName))
                {
                    WritePackageDetail(html, project.ProjectName, package);
                }

                html.AppendLine("            </div>");
                html.AppendLine("        </div>");
            }
        }

        private static void WritePackageDetail(StringBuilder html, string projectName, PackageUsageDetail package)
        {
            var isUnused = !package.UsedNamespaces.Any();
            var packageId = $"{projectName}-{package.PackageName}".Replace(".", "-");

            html.AppendLine("                <div class='package'>");
            html.AppendLine($"                    <div class='package-header' onclick='togglePackage(\"{packageId}\")'>");
            html.AppendLine($"                        <div><span class='package-name'>\ud83d\udce6 {package.PackageName}</span>");
            html.AppendLine($"                        <span class='package-version'>v{package.Version}</span></div>");
            html.AppendLine("                        <div class='package-stats'>");
            html.AppendLine($"                            <span>{package.UsedNamespaces.Count} namespace(s)</span>");
            html.AppendLine($"                            <span>{package.UsedTypes.Count} type(s)</span>");
            html.AppendLine($"                            <span>{package.Files.Count} file(s)</span>");
            html.AppendLine($"                            <span class='toggle-icon' id='icon-{packageId}'>\u25b6</span>");
            html.AppendLine("                        </div>");
            html.AppendLine("                    </div>");
            html.AppendLine($"                    <div class='package-detail' id='content-{packageId}'>");

            if (isUnused)
            {
                html.AppendLine("                        <div class='warning'>\u26a0\ufe0f This package is referenced but not used in any code files!</div>");
            }
            else
            {
                foreach (var ns in package.UsedNamespaces.OrderBy(n => n))
                {
                    html.AppendLine($"                        <div class='section-header'>\ud83d\udccb Namespace: {ns}</div>");
                    if (package.TypesByNamespace.ContainsKey(ns) && package.TypesByNamespace[ns].Any())
                    {
                        html.AppendLine("                        <ul class='type-list'>");
                        foreach (var type in package.TypesByNamespace[ns].OrderBy(t => t))
                            html.AppendLine($"                            <li class='type-item'>{type}</li>");
                        html.AppendLine("                        </ul>");
                    }
                    else
                    {
                        html.AppendLine("                        <p style='margin-left:15px;color:#95a5a6;font-size:13px;'>No specific types detected</p>");
                    }
                }
                if (package.Files.Any())
                {
                    html.AppendLine("                        <div class='file-list'><div class='section-header'>\ud83d\udcc4 Files:</div>");
                    foreach (var file in package.Files.OrderBy(f => f))
                        html.AppendLine($"                            <span class='file-item'>{file}</span>");
                    html.AppendLine("                        </div>");
                }
            }

            html.AppendLine("                    </div>");
            html.AppendLine("                </div>");
        }

        // ?? Migration compatibility ?????????????????????????????

        private static void WriteMigrationSection(StringBuilder html, List<ProjectAnalysisResult> results)
        {
            html.AppendLine("        <div class='migration-section'>");
            html.AppendLine("            <h2 class='migration-title'>\ud83d\ude80 Migration Compatibility Analysis</h2>");
            html.AppendLine("            <p class='migration-description'>Analysis of types and their compatibility with .NET Standard 2.0 / .NET Core / .NET 5+</p>");

            var allTypeDetails = results
                .SelectMany(r => r.PackageUsages)
                .SelectMany(p => p.TypeDetails.Values)
                .GroupBy(t => t.Name)
                .Select(g => g.First())
                .OrderBy(t => t.IsNetStandardCompatible ? 1 : 0)
                .ThenBy(t => t.Name)
                .ToList();

            var compatibleCount = allTypeDetails.Count(t => t.IsNetStandardCompatible);
            var blockedCount = allTypeDetails.Count(t => !t.IsNetStandardCompatible);

            html.AppendLine("            <div class='migration-summary'><div class='migration-stats'>");
            html.AppendLine($"                <div class='migration-stat compatible'><div class='stat-number'>{compatibleCount}</div><div class='stat-label'>\u2705 .NET Standard Compatible</div></div>");
            html.AppendLine($"                <div class='migration-stat blocked'><div class='stat-number'>{blockedCount}</div><div class='stat-label'>\u26a0\ufe0f Framework Dependencies</div></div>");
            html.AppendLine("            </div></div>");

            var blockedTypes = allTypeDetails.Where(t => !t.IsNetStandardCompatible).ToList();
            if (blockedTypes.Any())
            {
                html.AppendLine("            <div class='migration-group'>");
                html.AppendLine("                <h3 class='migration-group-title'>\u26a0\ufe0f Types with .NET Framework Dependencies</h3>");
                html.AppendLine("                <p class='migration-group-desc'>These types use .NET Framework-specific APIs and may require refactoring.</p>");
                foreach (var td in blockedTypes)
                {
                    var migTypeId = $"mig-{td.Name.Replace("<", "-").Replace(">", "-")}";
                    html.AppendLine($"                <div class='migration-type'>");
                    html.AppendLine($"                    <div class='migration-type-header' onclick='toggleMigrationType(\"{migTypeId}\")'>");
                    html.AppendLine($"                        <div class='migration-type-name'>\u274c {td.Name}</div>");
                    html.AppendLine($"                        <span class='toggle-icon' id='icon-{migTypeId}'>\u25b6</span>");
                    html.AppendLine("                    </div>");
                    html.AppendLine($"                    <div class='migration-type-detail' id='detail-{migTypeId}'>");
                    html.AppendLine($"                        <div><strong>Namespace:</strong> {td.Namespace}</div>");
                    html.AppendLine($"                        <div><strong>Assembly:</strong> {td.AssemblyName ?? "Unknown"}</div>");
                    html.AppendLine("                        <div class='dependency-list'><strong>Framework Dependencies:</strong><ul>");
                    foreach (var dep in td.FrameworkDependencies.Distinct())
                        html.AppendLine($"                            <li>{dep}</li>");
                    html.AppendLine("                        </ul></div></div></div>");
                }
                html.AppendLine("            </div>");
            }

            var compatibleTypes = allTypeDetails.Where(t => t.IsNetStandardCompatible).ToList();
            if (compatibleTypes.Any())
            {
                html.AppendLine("            <div class='migration-group'>");
                html.AppendLine("                <h3 class='migration-group-title'>\u2705 .NET Standard Compatible Types</h3>");
                html.AppendLine("                <p class='migration-group-desc'>These types should be easier to migrate.</p>");
                html.AppendLine("                <div class='compatible-types-grid'>");
                foreach (var td in compatibleTypes)
                {
                    html.AppendLine($"                    <div class='compatible-type-item'>");
                    html.AppendLine($"                        <div class='compatible-type-name'>\u2705 {td.Name}</div>");
                    html.AppendLine($"                        <div class='compatible-type-ns'>{td.Namespace}</div></div>");
                }
                html.AppendLine("                </div></div>");
            }

            html.AppendLine("        </div>");
        }

        // ?? DLL manifest analysis ???????????????????????????????

        private static void WriteManifestSection(StringBuilder html, List<DllManifestAnalysis> manifestResults)
        {
            if (manifestResults == null || !manifestResults.Any()) return;

            var manifestAnalyzed = manifestResults.Count(r => r.Error == null);
            var manifestBlockers = manifestResults.Count(r => r.Dependencies.Any(d => d.IsBlocker));
            var manifestClean = manifestResults.Count(r => r.Error == null && !r.Dependencies.Any(d => d.IsBlocker));
            var manifestErrors = manifestResults.Count(r => r.Error != null);

            html.AppendLine("        <div class='manifest-section'>");
            html.AppendLine("            <h2 class='manifest-title'>\ud83d\udd0e DLL Manifest Dependency Analysis</h2>");
            html.AppendLine("            <p class='manifest-description'>Assembly manifest inspection to detect .NET Framework-only dependencies.</p>");
            html.AppendLine("            <div class='manifest-summary'>");
            html.AppendLine("                <h3 style='margin-top:0;color:#2c3e50;'>Manifest Analysis Summary</h3>");
            html.AppendLine("                <div class='manifest-stats'>");
            html.AppendLine($"                    <div class='manifest-stat blocker'><div class='stat-number'>{manifestBlockers}</div><div class='stat-label'>\u274c With Blockers</div></div>");
            html.AppendLine($"                    <div class='manifest-stat clean'><div class='stat-number'>{manifestClean}</div><div class='stat-label'>\u2705 Clean</div></div>");
            html.AppendLine($"                    <div class='manifest-stat error'><div class='stat-number'>{manifestErrors}</div><div class='stat-label'>\u26a0\ufe0f Could Not Analyze</div></div>");
            html.AppendLine("                </div></div>");

            foreach (var manifest in manifestResults.OrderByDescending(r => r.Dependencies.Count(d => d.IsBlocker)).ThenBy(r => r.PackageName))
            {
                var hasBlockers = manifest.Dependencies.Any(d => d.IsBlocker);
                var hasError = manifest.Error != null;
                var headerClass = hasBlockers ? "has-blockers" : hasError ? "has-error" : "clean";
                var statusIcon = hasBlockers ? "\u274c" : hasError ? "\u26a0\ufe0f" : "\u2705";
                var manifestId = $"manifest-{manifest.PackageName.Replace(".", "-")}";

                html.AppendLine("                <div class='manifest-package'>");
                html.AppendLine($"                    <div class='manifest-package-header {headerClass}' onclick='toggleManifest(\"{manifestId}\")'>");
                html.AppendLine($"                        <div><div class='manifest-package-name'>{statusIcon} {manifest.PackageName}</div>");

                if (!string.IsNullOrEmpty(manifest.TargetFramework))
                    html.AppendLine($"                        <div class='manifest-package-meta'>Target Framework: {manifest.TargetFramework}</div>");

                if (hasError)
                    html.AppendLine($"                        <div class='manifest-package-meta'>\u26a0\ufe0f {manifest.Error}</div>");
                else
                {
                    var blockerDeps = manifest.Dependencies.Count(d => d.IsBlocker);
                    html.AppendLine($"                        <div class='manifest-package-meta'>{manifest.Dependencies.Count} referenced assemblies, {blockerDeps} blocker(s)</div>");
                }

                html.AppendLine($"                        </div><span class='toggle-icon' id='icon-{manifestId}'>\u25b6</span></div>");
                html.AppendLine($"                    <div class='manifest-package-detail' id='detail-{manifestId}'>");

                if (manifest.Dependencies.Any())
                {
                    if (!string.IsNullOrEmpty(manifest.DllPath))
                        html.AppendLine($"                        <div style='margin-bottom:15px;font-size:13px;color:#7f8c8d;'><strong>DLL Path:</strong> {manifest.DllPath}</div>");
                    if (!string.IsNullOrEmpty(manifest.AssemblyFullName))
                        html.AppendLine($"                        <div style='margin-bottom:15px;font-size:13px;color:#7f8c8d;'><strong>Assembly:</strong> {manifest.AssemblyFullName}</div>");

                    html.AppendLine("                        <table class='manifest-dep-table'>");
                    html.AppendLine("                            <thead><tr><th>Referenced Assembly</th><th>Version</th><th>Status</th><th>Details</th></tr></thead>");
                    html.AppendLine("                            <tbody>");
                    foreach (var dep in manifest.Dependencies.OrderByDescending(d => d.IsBlocker).ThenBy(d => d.AssemblyName))
                    {
                        var rowClass = dep.IsBlocker ? " class='blocker-row'" : "";
                        var badge = dep.IsBlocker ? "<span class='blocker-badge red'>\u274c BLOCKER</span>" : "<span class='blocker-badge green'>\u2705 OK</span>";
                        var details = dep.IsBlocker ? $"<strong>{dep.BlockerCategory}:</strong> {dep.BlockerReason}" : "";
                        html.AppendLine($"                            <tr{rowClass}><td><code>{dep.AssemblyName}</code></td><td>{dep.Version}</td><td>{badge}</td><td>{details}</td></tr>");
                    }
                    html.AppendLine("                            </tbody></table>");
                }

                html.AppendLine("                    </div></div>");
            }

            html.AppendLine("        </div>");
        }

        // ?? Type index ??????????????????????????????????????????

        private static void WriteTypeIndexSection(StringBuilder html, List<ProjectAnalysisResult> results)
        {
            var typeIndex = BuildTypeIndex(results);
            if (!typeIndex.Any()) return;

            html.AppendLine("        <div class='type-index-section'>");
            html.AppendLine("            <h2 class='type-index-title'>\ud83d\udd37 Type Index - All Types Used Across Solution</h2>");
            html.AppendLine("            <p class='type-index-description'>All unique types and where they are used.</p>");
            html.AppendLine("            <div class='type-index-search'><input type='text' id='typeSearch' placeholder='\ud83d\udd0d Search for a type...' onkeyup='filterTypes()' /></div>");
            html.AppendLine("            <div class='type-index-list'>");

            foreach (var typeEntry in typeIndex.OrderBy(t => t.Key))
            {
                var typeName = typeEntry.Key;
                var usages = typeEntry.Value;
                var typeId = $"type-{typeName.Replace("<", "-").Replace(">", "-")}";
                var projCount = usages.Select(u => u.ProjectName).Distinct().Count();
                var fileCount = usages.SelectMany(u => u.Files).Distinct().Count();

                html.AppendLine($"                <div class='type-index-item' data-typename='{typeName.ToLower()}'>");
                html.AppendLine($"                    <div class='type-index-header' onclick='toggleTypeDetail(\"{typeId}\")'>");
                html.AppendLine($"                        <div class='type-index-name'>{typeName}</div>");
                html.AppendLine("                        <div class='type-index-summary'>");
                html.AppendLine($"                            <span class='type-badge'>\ud83d\udce6 {usages.Select(u => u.PackageName).Distinct().Count()} package(s)</span>");
                html.AppendLine($"                            <span class='type-badge'>\ud83d\udcc1 {projCount} project(s)</span>");
                html.AppendLine($"                            <span class='type-badge'>\ud83d\udcc4 {fileCount} file(s)</span>");
                html.AppendLine($"                            <span class='toggle-icon' id='icon-{typeId}'>\u25b6</span>");
                html.AppendLine("                        </div></div>");
                html.AppendLine($"                    <div class='type-index-detail' id='detail-{typeId}'>");

                foreach (var pkgGroup in usages.GroupBy(u => u.PackageName).OrderBy(g => g.Key))
                {
                    html.AppendLine($"                        <div class='type-usage-package'><strong>Package:</strong> {pkgGroup.Key}<br/><strong>Namespace:</strong> {pkgGroup.First().Namespace}</div>");
                    foreach (var projGroup in pkgGroup.GroupBy(u => u.ProjectName).OrderBy(g => g.Key))
                    {
                        html.AppendLine($"                        <div class='type-usage-project'><span class='project-label'>\ud83d\udcc1 {projGroup.Key}</span>");
                        html.AppendLine("                            <div class='type-usage-files'>");
                        foreach (var file in projGroup.SelectMany(u => u.Files).Distinct().OrderBy(f => f))
                            html.AppendLine($"                                <span class='file-item'>{file}</span>");
                        html.AppendLine("                            </div></div>");
                    }
                }

                html.AppendLine("                    </div></div>");
            }

            html.AppendLine("            </div></div>");
        }

        // ?? JavaScript + close ??????????????????????????????????

        private static void WriteScriptsAndClose(StringBuilder html)
        {
            html.AppendLine("    </div>");
            html.AppendLine("    <script>");
            html.AppendLine("        function toggleProject(name){var c=document.getElementById('content-'+name);var i=document.getElementById('icon-'+name);if(c.style.display==='none'){c.style.display='block';i.classList.add('open');}else{c.style.display='none';i.classList.remove('open');}}");
            html.AppendLine("        function togglePackage(id){var c=document.getElementById('content-'+id);var i=document.getElementById('icon-'+id);c.classList.toggle('show');i.classList.toggle('open');}");
            html.AppendLine("        function toggleTypeDetail(id){var d=document.getElementById('detail-'+id);var i=document.getElementById('icon-'+id);if(d.style.display==='none'||!d.style.display){d.style.display='block';i.classList.add('open');}else{d.style.display='none';i.classList.remove('open');}}");
            html.AppendLine("        function toggleMigrationType(id){var d=document.getElementById('detail-'+id);var i=document.getElementById('icon-'+id);if(d.style.display==='none'||!d.style.display){d.style.display='block';i.classList.add('open');}else{d.style.display='none';i.classList.remove('open');}}");
            html.AppendLine("        function toggleManifest(id){var d=document.getElementById('detail-'+id);var i=document.getElementById('icon-'+id);if(d.style.display==='none'||!d.style.display){d.style.display='block';i.classList.add('open');}else{d.style.display='none';i.classList.remove('open');}}");
            html.AppendLine("        function filterTypes(){var input=document.getElementById('typeSearch').value.toLowerCase();var items=document.querySelectorAll('.type-index-item');items.forEach(function(item){item.style.display=item.getAttribute('data-typename').includes(input)?'':'none';});}");
            html.AppendLine("    </script>");
            html.AppendLine("</body></html>");
        }

        // ?? Helpers ?????????????????????????????????????????????

        private static Dictionary<string, List<TypeUsageInfo>> BuildTypeIndex(List<ProjectAnalysisResult> results)
        {
            var typeIndex = new Dictionary<string, List<TypeUsageInfo>>(StringComparer.OrdinalIgnoreCase);

            foreach (var project in results)
            {
                foreach (var package in project.PackageUsages)
                {
                    foreach (var nsEntry in package.TypesByNamespace)
                    {
                        foreach (var type in nsEntry.Value)
                        {
                            if (!typeIndex.ContainsKey(type))
                                typeIndex[type] = new List<TypeUsageInfo>();

                            typeIndex[type].Add(new TypeUsageInfo
                            {
                                TypeName = type,
                                Namespace = nsEntry.Key,
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
    }
}
