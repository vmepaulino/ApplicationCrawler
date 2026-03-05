using System;
using System.Collections.Generic;

namespace DotNetCrawler.Analyzers
{
    /// <summary>
    /// Known .NET Framework-only assemblies, namespaces, and NuGet packages.
    /// Used by multiple analyzers to detect migration blockers.
    /// </summary>
    internal static class FrameworkCompatibility
    {
        public static void SafeAdd(Dictionary<string, string> dict, string key, string value)
        {
            if (!dict.ContainsKey(key))
                dict[key] = value;
        }

        public static string IsFrameworkOnlyAssembly(string assemblyName)
        {
            var frameworkOnlyAssemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Entity Framework 6.x
                { "EntityFramework", "Entity Framework 6.x (Use EF Core for .NET Standard)" },
                { "EntityFramework.SqlServer", "Entity Framework 6.x" },
                { "EntityFramework.SqlServerCompact", "Entity Framework 6.x" },

                // ASP.NET (Classic)
                { "System.Web", "ASP.NET (Classic) - Migrate to ASP.NET Core" },
                { "System.Web.Mvc", "ASP.NET MVC - Migrate to ASP.NET Core MVC" },
                { "System.Web.Http", "ASP.NET Web API - Migrate to ASP.NET Core Web API" },
                { "System.Web.WebPages", "ASP.NET Web Pages - Migrate to Razor Pages" },
                { "System.Web.Razor", "ASP.NET Razor (Classic)" },
                { "System.Web.Optimization", "ASP.NET Bundling (.NET Framework only)" },
                { "System.Web.Extensions", "ASP.NET AJAX Extensions (.NET Framework only)" },
                { "System.Web.Services", "ASP.NET Web Services (ASMX) - Use Web API/gRPC" },
                { "System.Web.ApplicationServices", "ASP.NET Application Services (.NET Framework only)" },
                { "System.Web.DynamicData", "ASP.NET Dynamic Data (.NET Framework only)" },
                { "System.Web.Entity", "ASP.NET Entity Data Source (.NET Framework only)" },
                { "System.Web.Routing", "ASP.NET Routing (Classic) - Built into ASP.NET Core" },

                // WCF
                { "System.ServiceModel", "WCF - Consider gRPC or ASP.NET Core Web API" },
                { "System.ServiceModel.Web", "WCF Web HTTP - Migrate to ASP.NET Core Web API" },
                { "System.ServiceModel.Activation", "WCF Activation (.NET Framework only)" },

                // Windows-specific
                { "System.Windows.Forms", "Windows Forms" },
                { "PresentationCore", "WPF" },
                { "PresentationFramework", "WPF" },
                { "WindowsBase", "WPF" },

                // Configuration
                { "System.Configuration", ".NET Framework Configuration - Use Microsoft.Extensions.Configuration" },
                { "System.Configuration.ConfigurationManager", "ConfigurationManager (Limited .NET Standard support)" },

                // SignalR (old)
                { "Microsoft.AspNet.SignalR", "SignalR (Classic) - Migrate to ASP.NET Core SignalR" },
                { "Microsoft.AspNet.SignalR.Core", "SignalR (Classic)" },
                { "Microsoft.AspNet.SignalR.SystemWeb", "SignalR SystemWeb (.NET Framework only)" },

                // Identity (old)
                { "Microsoft.AspNet.Identity", "ASP.NET Identity (Classic) - Migrate to ASP.NET Core Identity" },
                { "Microsoft.AspNet.Identity.Core", "ASP.NET Identity (Classic)" },
                { "Microsoft.AspNet.Identity.EntityFramework", "ASP.NET Identity EF (Classic)" },
                { "Microsoft.AspNet.Identity.Owin", "ASP.NET Identity OWIN (Classic)" },

                // OWIN
                { "Microsoft.Owin", "OWIN (.NET Framework only)" },
                { "Microsoft.Owin.Host.SystemWeb", "OWIN SystemWeb Host (.NET Framework only)" },
                { "Microsoft.Owin.Security", "OWIN Security (.NET Framework only)" },
                { "Microsoft.Owin.Security.OAuth", "OWIN OAuth (.NET Framework only)" },
                { "Microsoft.Owin.Security.Cookies", "OWIN Cookie Auth (.NET Framework only)" },
                { "Owin", "OWIN interface (.NET Framework only)" },

                // Logging (framework-only versions)
                { "log4net", "log4net (versions < 2.0.8 are .NET Framework only)" },
                { "Common.Logging", "Common.Logging (.NET Framework only - Use Microsoft.Extensions.Logging)" },
                { "Common.Logging.Core", "Common.Logging (.NET Framework only)" },

                // IoC/DI Containers (old framework-only versions)
                { "Microsoft.Practices.Unity", "Unity (old) - Use Microsoft.Extensions.DependencyInjection or Unity 5+" },
                { "Microsoft.Practices.Unity.Configuration", "Unity Configuration (old, .NET Framework only)" },
                { "Microsoft.Practices.Unity.Interception", "Unity Interception (old, .NET Framework only)" },
                { "Ninject", "Ninject (versions < 3.3 are .NET Framework only)" },
                { "Ninject.Web.Common", "Ninject Web Common (.NET Framework only)" },
                { "StructureMap", "StructureMap (.NET Framework only - Use Lamar for .NET Core)" },
                { "Spring.Core", "Spring.NET (.NET Framework only)" },
                { "Spring.Web", "Spring.NET Web (.NET Framework only)" },
                { "Spring.Data", "Spring.NET Data (.NET Framework only)" },

                // ORM (framework-only versions)
                { "NHibernate", "NHibernate (versions < 5.2 are .NET Framework only)" },
                { "FluentNHibernate", "FluentNHibernate (.NET Framework only)" },
                { "Iesi.Collections", "Iesi.Collections (NHibernate dependency, .NET Framework only)" },

                // Enterprise Library
                { "EnterpriseLibrary", "Enterprise Library (.NET Framework only)" },
                { "Microsoft.Practices.EnterpriseLibrary.Common", "Enterprise Library (.NET Framework only)" },
                { "Microsoft.Practices.EnterpriseLibrary.Data", "Enterprise Library Data (.NET Framework only)" },
                { "Microsoft.Practices.EnterpriseLibrary.Logging", "Enterprise Library Logging (.NET Framework only)" },
                { "Microsoft.Practices.EnterpriseLibrary.ExceptionHandling", "Enterprise Library (.NET Framework only)" },
                { "Microsoft.Practices.EnterpriseLibrary.Caching", "Enterprise Library Caching (.NET Framework only)" },
                { "Microsoft.Practices.EnterpriseLibrary.Validation", "Enterprise Library Validation (.NET Framework only)" },
                { "Microsoft.Practices.EnterpriseLibrary.Security", "Enterprise Library Security (.NET Framework only)" },
                { "Microsoft.Practices.EnterpriseLibrary.PolicyInjection", "Enterprise Library (.NET Framework only)" },

                // Windows Azure (legacy)
                { "Microsoft.WindowsAzure.Storage", "Legacy Azure Storage - Use Azure.Storage.Blobs" },
                { "Microsoft.WindowsAzure.ConfigurationManager", "Legacy Azure Configuration" },
                { "Microsoft.WindowsAzure.ServiceRuntime", "Azure Cloud Services Runtime (.NET Framework only)" },
                { "Microsoft.WindowsAzure.Diagnostics", "Azure Cloud Services Diagnostics (.NET Framework only)" },

                // ASP.NET Web Optimization / Bundling
                { "Microsoft.AspNet.Web.Optimization", "ASP.NET Bundling (.NET Framework only)" },
                { "WebGrease", "WebGrease (.NET Framework only)" },
                { "Antlr3.Runtime", "ANTLR 3 (Classic, bundled with ASP.NET)" },

                // Misc .NET Framework-only
                { "Microsoft.Web.Infrastructure", "Microsoft.Web.Infrastructure (.NET Framework only)" },
                { "Microsoft.CodeDom.Providers.DotNetCompilerPlatform", "Roslyn CodeDom (.NET Framework only)" },
                { "Microsoft.Net.Compilers", ".NET Framework Compilers (not needed in .NET Core)" },
                { "DotNetOpenAuth", "DotNetOpenAuth (.NET Framework only)" },
                { "DotNetOpenAuth.Core", "DotNetOpenAuth (.NET Framework only)" },

                // Reporting
                { "Microsoft.ReportViewer.WebForms", "Report Viewer WebForms (.NET Framework only)" },
                { "Microsoft.ReportViewer.WinForms", "Report Viewer WinForms (.NET Framework only)" },
                { "Microsoft.ReportViewer.Common", "Report Viewer (.NET Framework only)" },
                { "CrystalDecisions.CrystalReports.Engine", "Crystal Reports (.NET Framework only)" },
                { "CrystalDecisions.Shared", "Crystal Reports (.NET Framework only)" },

                // Remoting / Runtime
                { "System.Runtime.Remoting", ".NET Remoting (.NET Framework only)" },
                { "System.EnterpriseServices", "COM+ Enterprise Services (.NET Framework only)" },

                // Workflow
                { "System.Workflow.Runtime", "Windows Workflow Foundation 3.x (.NET Framework only)" },
                { "System.Workflow.Activities", "Windows Workflow Foundation 3.x (.NET Framework only)" },
                { "System.Workflow.ComponentModel", "Windows Workflow Foundation 3.x (.NET Framework only)" },
                { "System.Activities", "Windows Workflow Foundation 4.x (.NET Framework only)" },
                { "System.Activities.Core.Presentation", "WF Designer (.NET Framework only)" },

                // Transactions (old)
                { "System.Transactions", ".NET Framework Transactions - Use System.Transactions in .NET Core 3.0+" }
            };

            return frameworkOnlyAssemblies.ContainsKey(assemblyName)
                ? frameworkOnlyAssemblies[assemblyName]
                : null;
        }

        public static Dictionary<string, string> GetFrameworkSpecificNamespaces()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "System.Data.Entity", "Entity Framework 6.x" },
                { "EntityFramework", "Entity Framework 6.x" },
                { "System.Web", "ASP.NET (Classic)" },
                { "System.Web.Mvc", "ASP.NET MVC" },
                { "System.Web.Http", "ASP.NET Web API" },
                { "System.Web.UI", "ASP.NET Web Forms" },
                { "System.ServiceModel", "WCF (Windows Communication Foundation)" },
                { "System.Windows.Forms", "Windows Forms" },
                { "System.Windows", "WPF" },
                { "System.Windows.Controls", "WPF" },
                { "System.Xaml", "XAML (WPF)" },
                { "System.Configuration", ".NET Framework Configuration" },
                { "System.Runtime.Remoting", ".NET Remoting" },
                { "System.EnterpriseServices", "COM+ Services" },
                { "System.Workflow", "Windows Workflow Foundation" },
                { "System.Activities", "Windows Workflow Foundation" },
                { "System.Deployment", "ClickOnce Deployment" },
                { "System.DirectoryServices", "Active Directory (Limited in .NET Standard)" },
                { "System.Drawing", "System.Drawing (Limited .NET Core support)" }
            };
        }

        public static Dictionary<string, string> GetFrameworkOnlyNuGetPackages()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Logging
            SafeAdd(dict, "log4net", "log4net (versions < 2.0.8 are .NET Framework only) - Use Microsoft.Extensions.Logging / Serilog / NLog");
            SafeAdd(dict, "Common.Logging", "Common.Logging (.NET Framework only) - Use Microsoft.Extensions.Logging");
            SafeAdd(dict, "Common.Logging.Core", "Common.Logging (.NET Framework only)");
            SafeAdd(dict, "Common.Logging.Log4Net", "Common.Logging log4net adapter (.NET Framework only)");
            SafeAdd(dict, "Common.Logging.NLog", "Common.Logging NLog adapter (.NET Framework only)");

            // ORM / Data Access
            SafeAdd(dict, "EntityFramework", "Entity Framework 6.x (versions < 6.3 .NET Framework only) - Use EF Core");
            SafeAdd(dict, "EntityFramework.SqlServer", "Entity Framework 6.x (.NET Framework only)");
            SafeAdd(dict, "EntityFramework.SqlServerCompact", "Entity Framework 6.x (.NET Framework only)");
            SafeAdd(dict, "NHibernate", "NHibernate (versions < 5.2 .NET Framework only) - Use EF Core or upgrade NHibernate");
            SafeAdd(dict, "FluentNHibernate", "FluentNHibernate (.NET Framework only)");
            SafeAdd(dict, "Iesi.Collections", "Iesi.Collections - NHibernate dependency (.NET Framework only)");
            SafeAdd(dict, "LinqToExcel", "LinqToExcel (.NET Framework only)");

            // IoC / Dependency Injection (old framework-only versions)
            SafeAdd(dict, "Microsoft.Practices.Unity", "Unity (old) - Use Microsoft.Extensions.DependencyInjection or Unity 5+");
            SafeAdd(dict, "Microsoft.Practices.Unity.Configuration", "Unity Configuration (old, .NET Framework only)");
            SafeAdd(dict, "Microsoft.Practices.Unity.Interception", "Unity Interception (old, .NET Framework only)");
            SafeAdd(dict, "Unity.Mvc", "Unity MVC integration (.NET Framework only)");
            SafeAdd(dict, "Unity.WebApi", "Unity Web API integration (.NET Framework only)");
            SafeAdd(dict, "Ninject", "Ninject (versions < 3.3 .NET Framework only)");
            SafeAdd(dict, "Ninject.Web.Common", "Ninject Web (.NET Framework only)");
            SafeAdd(dict, "Ninject.MVC3", "Ninject MVC (.NET Framework only)");
            SafeAdd(dict, "Ninject.MVC5", "Ninject MVC5 (.NET Framework only)");
            SafeAdd(dict, "StructureMap", "StructureMap (.NET Framework only) - Use Lamar for .NET Core");
            SafeAdd(dict, "StructureMap.MVC5", "StructureMap MVC5 (.NET Framework only)");
            SafeAdd(dict, "Spring.Core", "Spring.NET (.NET Framework only)");
            SafeAdd(dict, "Spring.Web", "Spring.NET Web (.NET Framework only)");
            SafeAdd(dict, "Spring.Data", "Spring.NET Data (.NET Framework only)");
            SafeAdd(dict, "Spring.Aop", "Spring.NET AOP (.NET Framework only)");

            // ASP.NET Classic
            SafeAdd(dict, "Microsoft.AspNet.Mvc", "ASP.NET MVC (Classic) - Migrate to ASP.NET Core MVC");
            SafeAdd(dict, "Microsoft.AspNet.WebApi", "ASP.NET Web API (Classic) - Migrate to ASP.NET Core");
            SafeAdd(dict, "Microsoft.AspNet.WebApi.Core", "ASP.NET Web API (Classic)");
            SafeAdd(dict, "Microsoft.AspNet.WebApi.Client", "ASP.NET Web API Client (Classic)");
            SafeAdd(dict, "Microsoft.AspNet.WebApi.WebHost", "ASP.NET Web API WebHost (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.WebApi.Owin", "ASP.NET Web API OWIN (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.WebPages", "ASP.NET Web Pages (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.Razor", "ASP.NET Razor (Classic, .NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.SignalR", "SignalR (Classic) - Migrate to ASP.NET Core SignalR");
            SafeAdd(dict, "Microsoft.AspNet.SignalR.Core", "SignalR (Classic)");
            SafeAdd(dict, "Microsoft.AspNet.SignalR.SystemWeb", "SignalR SystemWeb (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.SignalR.JS", "SignalR JS (Classic)");
            SafeAdd(dict, "Microsoft.AspNet.Identity.Core", "ASP.NET Identity (Classic) - Migrate to ASP.NET Core Identity");
            SafeAdd(dict, "Microsoft.AspNet.Identity.EntityFramework", "ASP.NET Identity EF (Classic)");
            SafeAdd(dict, "Microsoft.AspNet.Identity.Owin", "ASP.NET Identity OWIN (Classic)");
            SafeAdd(dict, "Microsoft.AspNet.Web.Optimization", "ASP.NET Bundling (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.FriendlyUrls", "ASP.NET Friendly URLs (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.Providers", "ASP.NET Universal Providers (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.Membership.OpenAuth", "ASP.NET OpenAuth (.NET Framework only)");

            // OWIN
            SafeAdd(dict, "Microsoft.Owin", "OWIN (.NET Framework only) - Middleware built into ASP.NET Core");
            SafeAdd(dict, "Microsoft.Owin.Host.SystemWeb", "OWIN SystemWeb Host (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Host.HttpListener", "OWIN HttpListener Host (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Security", "OWIN Security (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Security.OAuth", "OWIN OAuth (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Security.Cookies", "OWIN Cookie Auth (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Security.ActiveDirectory", "OWIN AD Auth (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Security.Google", "OWIN Google Auth (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Security.Facebook", "OWIN Facebook Auth (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Security.MicrosoftAccount", "OWIN Microsoft Auth (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Security.Twitter", "OWIN Twitter Auth (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Owin.Cors", "OWIN CORS (.NET Framework only)");
            SafeAdd(dict, "Owin", "OWIN interface (.NET Framework only)");

            // Enterprise Library
            SafeAdd(dict, "EnterpriseLibrary.Common", "Enterprise Library (.NET Framework only)");
            SafeAdd(dict, "EnterpriseLibrary.Data", "Enterprise Library Data (.NET Framework only) - Use Dapper / EF Core");
            SafeAdd(dict, "EnterpriseLibrary.Logging", "Enterprise Library Logging (.NET Framework only) - Use Microsoft.Extensions.Logging");
            SafeAdd(dict, "EnterpriseLibrary.ExceptionHandling", "Enterprise Library Exception Handling (.NET Framework only)");
            SafeAdd(dict, "EnterpriseLibrary.Caching", "Enterprise Library Caching (.NET Framework only) - Use IMemoryCache");
            SafeAdd(dict, "EnterpriseLibrary.Validation", "Enterprise Library Validation (.NET Framework only) - Use FluentValidation");
            SafeAdd(dict, "EnterpriseLibrary.Security.Cryptography", "Enterprise Library Crypto (.NET Framework only)");
            SafeAdd(dict, "EnterpriseLibrary.PolicyInjection", "Enterprise Library Policy Injection (.NET Framework only)");
            SafeAdd(dict, "EnterpriseLibrary.TransientFaultHandling", "Enterprise Library Transient Fault Handling (.NET Framework only) - Use Polly");
            SafeAdd(dict, "Microsoft.Practices.EnterpriseLibrary.Common", "Enterprise Library (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Practices.EnterpriseLibrary.Data", "Enterprise Library Data (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Practices.EnterpriseLibrary.Logging", "Enterprise Library Logging (.NET Framework only)");

            // Azure (legacy SDKs)
            SafeAdd(dict, "Microsoft.WindowsAzure.Storage", "Legacy Azure Storage - Use Azure.Storage.Blobs");
            SafeAdd(dict, "Microsoft.WindowsAzure.ConfigurationManager", "Legacy Azure Configuration");
            SafeAdd(dict, "Microsoft.WindowsAzure.ServiceRuntime", "Azure Cloud Services (.NET Framework only)");
            SafeAdd(dict, "WindowsAzure.Storage", "Legacy Azure Storage - Use Azure.Storage.Blobs");
            SafeAdd(dict, "WindowsAzure.ServiceBus", "Legacy Azure Service Bus - Use Azure.Messaging.ServiceBus");

            // Web / HTTP (old)
            SafeAdd(dict, "Microsoft.Web.Infrastructure", "Microsoft.Web.Infrastructure (.NET Framework only)");
            SafeAdd(dict, "WebGrease", "WebGrease (.NET Framework only)");
            SafeAdd(dict, "Antlr", "ANTLR (Classic ASP.NET bundling dependency)");

            // Authentication (old)
            SafeAdd(dict, "DotNetOpenAuth", "DotNetOpenAuth (.NET Framework only)");
            SafeAdd(dict, "DotNetOpenAuth.Core", "DotNetOpenAuth (.NET Framework only)");
            SafeAdd(dict, "DotNetOpenAuth.AspNet", "DotNetOpenAuth ASP.NET (.NET Framework only)");

            // Build / Compilation
            SafeAdd(dict, "Microsoft.CodeDom.Providers.DotNetCompilerPlatform", "Roslyn CodeDom (.NET Framework only)");
            SafeAdd(dict, "Microsoft.Net.Compilers", ".NET Framework Compilers (not needed in .NET Core)");
            SafeAdd(dict, "Microsoft.Net.Compilers.Toolset", ".NET Framework Compilers (not needed in .NET Core)");

            // Reporting
            SafeAdd(dict, "Microsoft.ReportViewer.WebForms", "Report Viewer WebForms (.NET Framework only)");
            SafeAdd(dict, "Microsoft.ReportViewer.WinForms", "Report Viewer WinForms (.NET Framework only)");
            SafeAdd(dict, "Microsoft.ReportViewer.Common", "Report Viewer (.NET Framework only)");
            SafeAdd(dict, "CrystalDecisions.CrystalReports.Engine", "Crystal Reports (.NET Framework only)");

            // WCF
            SafeAdd(dict, "System.ServiceModel", "WCF (.NET Framework only) - Use gRPC or ASP.NET Core Web API");

            // Misc
            SafeAdd(dict, "System.Web.Optimization", "ASP.NET Bundling (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.ScriptManager.MSAjax", "ASP.NET AJAX (.NET Framework only)");
            SafeAdd(dict, "Microsoft.AspNet.ScriptManager.WebForms", "ASP.NET WebForms Scripts (.NET Framework only)");
            SafeAdd(dict, "AjaxControlToolkit", "ASP.NET AJAX Control Toolkit (.NET Framework only)");

            return dict;
        }
    }
}
