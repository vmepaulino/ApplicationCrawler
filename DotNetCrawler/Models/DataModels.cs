using System.Collections.Generic;

namespace DotNetCrawler.Models
{
    internal class ParsedArguments
    {
        public string SolutionPath { get; set; }
        public string[] DependencyPatterns { get; set; }
        public string OutputFile { get; set; }
        public string[] BinPaths { get; set; }
    }

    internal class ProjectInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public List<PackageRef> Packages { get; set; }

        public ProjectInfo()
        {
            Packages = new List<PackageRef>();
        }
    }

    internal class PackageRef
    {
        public string Name { get; set; }
        public string Version { get; set; }

        public PackageRef(string name, string version)
        {
            Name = name;
            Version = version;
        }
    }

    internal class ProjectAnalysisResult
    {
        public string ProjectName { get; set; }
        public string ProjectPath { get; set; }
        public List<PackageUsageDetail> PackageUsages { get; set; }

        public ProjectAnalysisResult()
        {
            PackageUsages = new List<PackageUsageDetail>();
        }
    }

    internal class PackageUsageDetail
    {
        public string PackageName { get; set; }
        public string Version { get; set; }
        public HashSet<string> UsedNamespaces { get; set; }
        public Dictionary<string, HashSet<string>> TypesByNamespace { get; set; }
        public HashSet<string> UsedTypes { get; set; }
        public HashSet<string> Files { get; set; }
        public Dictionary<string, TypeDetail> TypeDetails { get; set; }
        public string PackageTargetFramework { get; set; }
        public List<AssemblyDependency> PackageAssemblyDependencies { get; set; }

        public PackageUsageDetail()
        {
            UsedNamespaces = new HashSet<string>();
            TypesByNamespace = new Dictionary<string, HashSet<string>>();
            UsedTypes = new HashSet<string>();
            Files = new HashSet<string>();
            TypeDetails = new Dictionary<string, TypeDetail>();
            PackageAssemblyDependencies = new List<AssemblyDependency>();
        }
    }

    internal class TypeDetail
    {
        public string Name { get; set; }
        public string Namespace { get; set; }
        public string AssemblyName { get; set; }
        public List<string> FrameworkDependencies { get; set; }
        public bool IsNetStandardCompatible { get; set; }

        public TypeDetail()
        {
            FrameworkDependencies = new List<string>();
            IsNetStandardCompatible = true;
        }
    }

    internal class TypeUsageInfo
    {
        public string TypeName { get; set; }
        public string Namespace { get; set; }
        public string PackageName { get; set; }
        public string ProjectName { get; set; }
        public List<string> Files { get; set; }

        public TypeUsageInfo()
        {
            Files = new List<string>();
        }
    }

    internal class AssemblyAnalysisInfo
    {
        public string PackageName { get; set; }
        public string TargetFramework { get; set; }
        public List<AssemblyDependency> Dependencies { get; set; }

        public AssemblyAnalysisInfo()
        {
            Dependencies = new List<AssemblyDependency>();
        }
    }

    internal class AssemblyDependency
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public bool IsFrameworkOnly { get; set; }
        public string FrameworkOnlyReason { get; set; }
    }

    internal class DllManifestAnalysis
    {
        public string PackageName { get; set; }
        public string DllPath { get; set; }
        public string AssemblyFullName { get; set; }
        public string TargetFramework { get; set; }
        public string Error { get; set; }
        public List<ManifestDependencyInfo> Dependencies { get; set; }

        public DllManifestAnalysis()
        {
            Dependencies = new List<ManifestDependencyInfo>();
        }
    }

    internal class ManifestDependencyInfo
    {
        public string AssemblyName { get; set; }
        public string Version { get; set; }
        public bool IsBlocker { get; set; }
        public string BlockerReason { get; set; }
        public string BlockerCategory { get; set; }
    }
}
