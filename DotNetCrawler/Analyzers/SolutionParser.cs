using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotNetCrawler.Models;

namespace DotNetCrawler.Analyzers
{
    internal static class SolutionParser
    {
        public static List<string> ParseSolutionFile(string solutionPath)
        {
            var projectPaths = new List<string>();

            try
            {
                var lines = File.ReadAllLines(solutionPath);
                var projectLineRegex = new Regex(
                    @"Project\(""\{[A-F0-9-]+\}""\)\s*=\s*""[^""]+"",\s*""([^""]+\.csproj)""",
                    RegexOptions.IgnoreCase);

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
                Console.WriteLine($"??  Error parsing solution file: {ex.Message}");
            }

            return projectPaths;
        }

        public static List<PackageRef> GetNuGetPackagesFromProjectFile(string projectFilePath)
        {
            var packages = new List<PackageRef>();

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

                packages.AddRange(packageRefs.Select(p => new PackageRef(p.Name, p.Version ?? "unknown")));

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

                    packages.AddRange(packagesFromConfig.Select(p => new PackageRef(p.Name, p.Version ?? "unknown")));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ??  Warning: Could not read packages: {ex.Message}");
            }

            return packages;
        }
    }
}
