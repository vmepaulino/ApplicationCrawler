using System.Text.RegularExpressions;
using AngularAppAnalyser.Abstractions;
using AngularAppAnalyser.Models;

namespace AngularAppAnalyser.Analyzers;

internal static class ApiCommunicationAnalyzer
{
    public static List<Finding> Analyze(AnalysisContext ctx)
    {
        var findings = new List<Finding>();
        var appPath = ctx.AppPath;
        var tsFiles = ctx.GetSourceFiles("*.ts");

        var hasInterceptor = false;
        var hasAuthInterceptor = false;
        var hasErrorInterceptor = false;
        var hasRetryLogic = false;
        var hasCsrfProtection = false;

        var apiPatterns = new List<SecurityPattern>
        {
            new(@"this\.http\.(get|post|put|delete|patch)\s*(<[^>]*>)?\s*\(\s*[`'""][^)]*[`'""]", "Hardcoded API URL", "API URL appears hardcoded. Use environment configuration for API base URLs to support different deployment environments.", Severity.Medium, "API Communication"),
            new(@"\.pipe\s*\([^)]*catchError", "Error handling in HTTP pipe", "HTTP error handling detected. Ensure errors don't leak sensitive server information to the user.", Severity.Info, "API Communication"),
            new(@"withCredentials\s*:\s*true", "withCredentials enabled", "withCredentials sends cookies cross-origin. Ensure CORS is properly configured on the server.", Severity.Medium, "API Communication"),
            new(@"'Content-Type'\s*:\s*'application/x-www-form-urlencoded'", "URL-encoded form data", "URL-encoded form submissions detected. Prefer JSON payloads with proper Content-Type headers.", Severity.Low, "API Communication"),
            new(@"new\s+XMLHttpRequest", "Raw XMLHttpRequest", "Raw XMLHttpRequest usage bypasses Angular's HttpClient and its interceptor pipeline. Use HttpClient instead.", Severity.High, "API Communication"),
            new(@"fetch\s*\(", "fetch() API usage", "Native fetch() bypasses Angular's HttpClient interceptors. Use HttpClient for consistent error handling and auth.", Severity.Medium, "API Communication"),
            new(@"\.subscribe\s*\(\s*\w+\s*=>\s*\{[^}]*\}\s*\)\s*;", "Subscribe without error handler", "HTTP subscribe without error callback. Add error handling to prevent unhandled promise rejections.", Severity.Medium, "API Communication"),
        };

        foreach (var file in tsFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                var relativePath = Path.GetRelativePath(appPath, file);
                var lines = content.Split('\n');

                if (Regex.IsMatch(content, @"implements\s+HttpInterceptor|HttpInterceptorFn", RegexOptions.IgnoreCase))
                {
                    hasInterceptor = true;
                    if (Regex.IsMatch(content, @"(authorization|bearer|auth[_-]?token|x-auth)", RegexOptions.IgnoreCase))
                        hasAuthInterceptor = true;
                    if (Regex.IsMatch(content, @"(catchError|HttpErrorResponse|error)", RegexOptions.IgnoreCase))
                        hasErrorInterceptor = true;
                    if (Regex.IsMatch(content, @"(X-XSRF-TOKEN|X-CSRF-TOKEN|csrf|xsrf)", RegexOptions.IgnoreCase))
                        hasCsrfProtection = true;
                }

                if (Regex.IsMatch(content, @"(retry|retryWhen|RetryConfig)", RegexOptions.IgnoreCase))
                    hasRetryLogic = true;

                foreach (var pattern in apiPatterns)
                {
                    var matches = Regex.Matches(content, pattern.Pattern, RegexOptions.IgnoreCase);
                    foreach (Match match in matches)
                    {
                        var lineNumber = content[..match.Index].Count(c => c == '\n') + 1;
                        var lineContent = lineNumber <= lines.Length ? lines[lineNumber - 1].Trim() : "";

                        if (lineContent.TrimStart().StartsWith("//") || lineContent.TrimStart().StartsWith("*"))
                            continue;

                        findings.Add(new Finding
                        {
                            Title = pattern.Title,
                            Description = pattern.Description,
                            Severity = pattern.Severity,
                            Category = pattern.Category,
                            File = relativePath,
                            Line = lineNumber,
                            CodeSnippet = lineContent.Length > 120 ? lineContent[..120] + "..." : lineContent
                        });
                    }
                }
            }
            catch { }
        }

        if (!hasInterceptor)
            findings.Add(new Finding { Title = "No HTTP Interceptor found", Description = "No HttpInterceptor implementation detected. Interceptors are essential for centralized auth token injection, error handling, logging, and retry logic.", Severity = Severity.High, Category = "API Communication" });

        if (hasInterceptor && !hasAuthInterceptor)
            findings.Add(new Finding { Title = "No auth interceptor detected", Description = "HTTP interceptors exist but none appear to handle authentication headers. Implement an auth interceptor for consistent token management.", Severity = Severity.High, Category = "API Communication" });

        if (hasInterceptor && !hasErrorInterceptor)
            findings.Add(new Finding { Title = "No error interceptor detected", Description = "No centralized HTTP error handling interceptor found. Implement an error interceptor to handle 401/403 redirects and user-facing error messages.", Severity = Severity.Medium, Category = "API Communication" });

        if (!hasCsrfProtection)
            findings.Add(new Finding { Title = "No CSRF/XSRF protection detected", Description = "No CSRF token handling detected. Angular's HttpClient supports XSRF protection via HttpClientXsrfModule.", Severity = Severity.High, Category = "API Communication" });

        if (!hasRetryLogic)
            findings.Add(new Finding { Title = "No HTTP retry logic detected", Description = "No retry logic found for HTTP calls. Consider implementing retry with exponential backoff for transient failures.", Severity = Severity.Low, Category = "API Communication" });

        return findings;
    }

    public static List<ApiEndpointInfo> ExtractEndpoints(AnalysisContext ctx)
    {
        var endpoints = new List<ApiEndpointInfo>();
        var appPath = ctx.AppPath;
        var tsFiles = ctx.GetSourceFiles("*.ts");

        var httpCallPattern = @"this\.\w+\.(get|post|put|delete|patch|head|options)\s*(?:<[^>]*>)?\s*\(\s*([`'""])(.*?)\2";

        foreach (var file in tsFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                var relativePath = Path.GetRelativePath(appPath, file);
                var lines = content.Split('\n');

                var serviceMatch = Regex.Match(content, @"export\s+class\s+(\w+)");
                var serviceName = serviceMatch.Success ? serviceMatch.Groups[1].Value : Path.GetFileNameWithoutExtension(file);

                foreach (Match match in Regex.Matches(content, httpCallPattern, RegexOptions.Singleline))
                {
                    var lineNumber = content[..match.Index].Count(c => c == '\n') + 1;
                    var lineContent = lineNumber <= lines.Length ? lines[lineNumber - 1].Trim() : "";
                    if (lineContent.TrimStart().StartsWith("//")) continue;

                    endpoints.Add(new ApiEndpointInfo { HttpMethod = match.Groups[1].Value.ToUpper(), Url = match.Groups[3].Value, File = relativePath, Line = lineNumber, ServiceName = serviceName });
                }

                foreach (Match match in Regex.Matches(content, @"fetch\s*\(\s*([`'""])(.*?)\1", RegexOptions.Singleline))
                {
                    var lineNumber = content[..match.Index].Count(c => c == '\n') + 1;
                    var lineContent = lineNumber <= lines.Length ? lines[lineNumber - 1].Trim() : "";
                    if (lineContent.TrimStart().StartsWith("//")) continue;

                    endpoints.Add(new ApiEndpointInfo { HttpMethod = "FETCH", Url = match.Groups[2].Value, File = relativePath, Line = lineNumber, ServiceName = serviceName });
                }
            }
            catch { }
        }

        return endpoints;
    }
}
