using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AngularAppAnalyser;

internal class Program
{
    static int Main(string[] args)
    {
        try
        {
            var parsedArgs = ParseArguments(args);
            if (parsedArgs is null)
            {
                ShowUsage();
                return 1;
            }

            RunAnalysis(parsedArgs);
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static void RunAnalysis(ParsedArguments args)
    {
        var stopwatch = Stopwatch.StartNew();
        var appPath = Path.GetFullPath(args.AppPath);

        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"🔍 Angular App Analyser");
        Console.WriteLine($"   Path: {appPath}");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine();

        if (!Directory.Exists(appPath))
        {
            Console.Error.WriteLine($"Error: Directory not found: {appPath}");
            return;
        }

        var packageJsonPath = Path.Combine(appPath, "package.json");
        if (!File.Exists(packageJsonPath))
        {
            Console.Error.WriteLine($"Error: package.json not found in {appPath}. Is this an Angular project?");
            return;
        }

        var report = new AnalysisReport { AppPath = appPath, Timestamp = DateTime.Now };

        // Step 1: Parse project metadata
        Console.WriteLine("[Step 1/8] 📦 Parsing project metadata...");
        report.ProjectMetadata = AnalyzeProjectMetadata(appPath);
        Console.WriteLine($"[Step 1/8] ✅ Angular v{report.ProjectMetadata.AngularVersion ?? "unknown"}, {report.ProjectMetadata.Dependencies.Count} deps, {report.ProjectMetadata.DevDependencies.Count} devDeps ({stopwatch.ElapsedMilliseconds}ms)");
        Console.WriteLine();

        // Step 2: Scan for security vulnerabilities
        Console.WriteLine("[Step 2/8] 🛡️ Scanning for security vulnerabilities...");
        var stepStart = stopwatch.ElapsedMilliseconds;
        report.SecurityFindings = AnalyzeSecurity(appPath);
        var criticalCount = report.SecurityFindings.Count(f => f.Severity == Severity.Critical);
        var highCount = report.SecurityFindings.Count(f => f.Severity == Severity.High);
        Console.WriteLine($"[Step 2/8] ✅ {report.SecurityFindings.Count} finding(s) — {criticalCount} critical, {highCount} high ({stopwatch.ElapsedMilliseconds - stepStart}ms)");
        Console.WriteLine();

        // Step 3: Analyze storage usage
        Console.WriteLine("[Step 3/8] 💾 Analyzing storage usage...");
        stepStart = stopwatch.ElapsedMilliseconds;
        report.StorageFindings = AnalyzeStorage(appPath);
        Console.WriteLine($"[Step 3/8] ✅ {report.StorageFindings.Count} storage usage(s) found ({stopwatch.ElapsedMilliseconds - stepStart}ms)");
        Console.WriteLine();

        // Step 4: Analyze API communication + extract endpoints
        Console.WriteLine("[Step 4/8] 🌐 Analyzing API communication & extracting endpoints...");
        stepStart = stopwatch.ElapsedMilliseconds;
        report.ApiFindings = AnalyzeApiCommunication(appPath);
        report.ApiEndpoints = ExtractApiEndpoints(appPath);
        Console.WriteLine($"[Step 4/8] ✅ {report.ApiFindings.Count} finding(s), {report.ApiEndpoints.Count} API endpoint(s) discovered ({stopwatch.ElapsedMilliseconds - stepStart}ms)");
        Console.WriteLine();

        // Step 5: Analyze application design + browser resources
        Console.WriteLine("[Step 5/8] 🏗️ Analyzing application design & browser resources...");
        stepStart = stopwatch.ElapsedMilliseconds;
        report.DesignFindings = AnalyzeDesign(appPath);
        Console.WriteLine($"[Step 5/8] ✅ {report.DesignFindings.Count} finding(s) ({stopwatch.ElapsedMilliseconds - stepStart}ms)");
        Console.WriteLine();

        // Step 6: Analyze library health
        Console.WriteLine("[Step 6/8] 📚 Analyzing library health...");
        stepStart = stopwatch.ElapsedMilliseconds;
        report.LibraryFindings = AnalyzeLibraries(report.ProjectMetadata);
        Console.WriteLine($"[Step 6/8] ✅ {report.LibraryFindings.Count} finding(s) ({stopwatch.ElapsedMilliseconds - stepStart}ms)");
        Console.WriteLine();

        // Step 7: Check library versions via npm outdated
        Console.WriteLine("[Step 7/8] 🔄 Checking library versions (npm outdated)...");
        stepStart = stopwatch.ElapsedMilliseconds;
        report.LibraryVersions = CheckLibraryVersions(appPath);
        var outdatedMajor = report.LibraryVersions.Count(v => v.UpdateType == "major");
        Console.WriteLine($"[Step 7/8] ✅ {report.LibraryVersions.Count} outdated package(s), {outdatedMajor} major update(s) ({stopwatch.ElapsedMilliseconds - stepStart}ms)");
        Console.WriteLine();

        // Step 8: Build security posture
        Console.WriteLine("[Step 8/8] 🔐 Building security posture summary...");
        stepStart = stopwatch.ElapsedMilliseconds;
        report.SecurityPosture = BuildSecurityPosture(appPath, report);
        var inPlaceCount = report.SecurityPosture.Count(p => p.InPlace);
        Console.WriteLine($"[Step 8/8] ✅ {inPlaceCount}/{report.SecurityPosture.Count} security controls in place ({stopwatch.ElapsedMilliseconds - stepStart}ms)");
        Console.WriteLine();

        // Console summary
        DisplayConsoleSummary(report);

        // Generate HTML report
        if (!string.IsNullOrEmpty(args.OutputFile))
        {
            Console.WriteLine("📝 Generating HTML report...");
            GenerateHtmlReport(report, args.OutputFile);
            Console.WriteLine($"📄 Report saved: {Path.GetFullPath(args.OutputFile)}");
        }

        Console.WriteLine($"\n⏱️  Total elapsed time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine("✨ Analysis complete!");
    }

    // ───────────────────────────────────────────────────────────
    // Step 1: Project Metadata
    // ───────────────────────────────────────────────────────────

    static ProjectMetadata AnalyzeProjectMetadata(string appPath)
    {
        var meta = new ProjectMetadata();
        var packageJsonPath = Path.Combine(appPath, "package.json");

        try
        {
            var json = File.ReadAllText(packageJsonPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            meta.Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            meta.Version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";

            if (root.TryGetProperty("dependencies", out var deps))
            {
                foreach (var prop in deps.EnumerateObject())
                    meta.Dependencies[prop.Name] = prop.Value.GetString() ?? "";
            }

            if (root.TryGetProperty("devDependencies", out var devDeps))
            {
                foreach (var prop in devDeps.EnumerateObject())
                    meta.DevDependencies[prop.Name] = prop.Value.GetString() ?? "";
            }

            // Determine Angular version
            if (meta.Dependencies.TryGetValue("@angular/core", out var angularVersion))
                meta.AngularVersion = angularVersion.TrimStart('^', '~');
            else if (meta.DevDependencies.TryGetValue("@angular/core", out angularVersion))
                meta.AngularVersion = angularVersion.TrimStart('^', '~');
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️  Error parsing package.json: {ex.Message}");
        }

        // Parse angular.json
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
                        Console.WriteLine($"   📁 Angular project: {proj.Name}");

                        // Check budgets
                        if (proj.Value.TryGetProperty("architect", out var architect) &&
                            architect.TryGetProperty("build", out var build) &&
                            build.TryGetProperty("configurations", out var configs) &&
                            configs.TryGetProperty("production", out var prod) &&
                            prod.TryGetProperty("budgets", out var budgets))
                        {
                            meta.HasBudgets = true;
                        }

                        // Check if standalone or module-based
                        if (proj.Value.TryGetProperty("architect", out var arch) &&
                            arch.TryGetProperty("build", out var b) &&
                            b.TryGetProperty("options", out var opts))
                        {
                            // Angular 17+ standalone by default check
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️  Error parsing angular.json: {ex.Message}");
            }
        }

        // Check for tsconfig.json
        meta.HasTsConfig = File.Exists(Path.Combine(appPath, "tsconfig.json"));
        meta.HasTsConfigApp = File.Exists(Path.Combine(appPath, "tsconfig.app.json"));

        return meta;
    }

    // ───────────────────────────────────────────────────────────
    // Step 2: Security Analysis
    // ───────────────────────────────────────────────────────────

    static List<Finding> AnalyzeSecurity(string appPath)
    {
        var findings = new List<Finding>();
        var tsFiles = GetSourceFiles(appPath, "*.ts");
        var htmlFiles = GetSourceFiles(appPath, "*.html");
        var fileIndex = 0;
        var totalFiles = tsFiles.Count + htmlFiles.Count;

        Console.WriteLine($"   Scanning {totalFiles} file(s)...");

        // Security patterns to check in TypeScript files
        var tsSecurityPatterns = new List<SecurityPattern>
        {
            new("innerHTML\\s*=", "Direct innerHTML assignment", "Direct innerHTML assignment bypasses Angular sanitization. Use [innerHTML] binding or DomSanitizer.", Severity.Critical, "Security"),
            new("bypassSecurityTrustHtml", "bypassSecurityTrustHtml usage", "bypassSecurityTrustHtml disables XSS protection. Ensure input is properly validated before bypassing.", Severity.Critical, "Security"),
            new("bypassSecurityTrustScript", "bypassSecurityTrustScript usage", "bypassSecurityTrustScript disables script sanitization. This is extremely dangerous.", Severity.Critical, "Security"),
            new("bypassSecurityTrustUrl", "bypassSecurityTrustUrl usage", "bypassSecurityTrustUrl disables URL sanitization. Validate URLs before bypassing.", Severity.High, "Security"),
            new("bypassSecurityTrustResourceUrl", "bypassSecurityTrustResourceUrl usage", "bypassSecurityTrustResourceUrl disables resource URL sanitization.", Severity.High, "Security"),
            new("bypassSecurityTrustStyle", "bypassSecurityTrustStyle usage", "bypassSecurityTrustStyle disables style sanitization.", Severity.Medium, "Security"),
            new(@"document\.cookie", "Direct cookie access", "Direct document.cookie access. Use a secure cookie service or HttpOnly cookies set by the server.", Severity.High, "Security"),
            new(@"eval\s*\(", "eval() usage", "eval() executes arbitrary code and is a major XSS vector. Remove eval() calls.", Severity.Critical, "Security"),
            new(@"new\s+Function\s*\(", "new Function() usage", "new Function() is equivalent to eval(). Remove dynamic function creation.", Severity.Critical, "Security"),
            new(@"document\.write", "document.write usage", "document.write can introduce XSS vulnerabilities. Use Angular rendering.", Severity.High, "Security"),
            new(@"window\.open\s*\(", "window.open usage", "window.open can be used for phishing. Validate target URLs.", Severity.Medium, "Security"),
            new(@"\bhttp://", "Insecure HTTP URL", "HTTP URLs transmit data in plaintext. Use HTTPS for all API and resource URLs.", Severity.High, "Security"),
            new(@"password.*=.*['""][^'""]+['""]", "Hardcoded password", "Possible hardcoded password detected. Store credentials securely, never in source code.", Severity.Critical, "Security"),
            new(@"(api[_-]?key|apikey|secret[_-]?key|access[_-]?token)\s*[:=]\s*['""][^'""]+['""]", "Hardcoded API key/secret", "Possible hardcoded API key or secret. Use environment variables.", Severity.Critical, "Security"),
            new(@"(?<!//).*(?:Bearer\s+)[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+", "Hardcoded JWT token", "Possible hardcoded JWT token. Tokens should never be stored in source code.", Severity.Critical, "Security"),
            new(@"allowJs\s*:\s*true", "TypeScript allowJs enabled", "allowJs weakens type safety. Consider migrating JS files to TypeScript.", Severity.Low, "Security"),
            new(@"@HostListener\s*\(\s*['""]message['""]", "postMessage listener", "postMessage listener detected. Validate message origin to prevent cross-origin attacks.", Severity.High, "Security"),
            new(@"window\.addEventListener\s*\(\s*['""]message['""]", "postMessage listener (raw)", "Raw postMessage event listener. Validate event.origin before processing.", Severity.High, "Security"),
            new(@"this\.http\.(get|post|put|delete|patch)\s*\(\s*[`'""]http://", "Insecure HTTP API call", "API call uses HTTP instead of HTTPS. All API calls should use HTTPS.", Severity.Critical, "Security"),
            new(@"\.subscribe\s*\(\s*\)", "Empty subscribe", "Empty subscribe() with no error handler. Unhandled HTTP errors can leak information.", Severity.Medium, "Security"),
        };

        // HTML security patterns
        var htmlSecurityPatterns = new List<SecurityPattern>
        {
            new(@"\[innerHTML\]\s*=", "[innerHTML] binding", "[innerHTML] binding detected. Ensure bound content is sanitized.", Severity.Medium, "Security"),
            new(@"<\s*iframe", "iframe usage", "iframe detected. Ensure src is validated and consider sandbox attribute.", Severity.High, "Security"),
            new(@"<\s*object\b", "object tag usage", "HTML object tag detected. This can load external content and is a security risk.", Severity.High, "Security"),
            new(@"<\s*embed\b", "embed tag usage", "HTML embed tag detected. This can load external content and is a security risk.", Severity.High, "Security"),
            new(@"javascript\s*:", "javascript: URL scheme", "javascript: URL scheme is an XSS vector. Use Angular routing instead.", Severity.Critical, "Security"),
            new(@"on\w+\s*=\s*['""]", "Inline event handler", "Inline event handlers bypass Angular's security context. Use Angular event bindings.", Severity.High, "Security"),
            new(@"<\s*form[^>]*action\s*=\s*['""]http://", "Insecure form action", "Form action uses HTTP. Use HTTPS for form submissions.", Severity.High, "Security"),
            new(@"style\s*=\s*['""].*expression\s*\(", "CSS expression", "CSS expression() detected. This is an XSS vector in older browsers.", Severity.High, "Security"),
        };

        foreach (var file in tsFiles)
        {
            fileIndex++;
            try
            {
                var content = File.ReadAllText(file);
                var relativePath = Path.GetRelativePath(appPath, file);
                var lines = content.Split('\n');

                foreach (var pattern in tsSecurityPatterns)
                {
                    var matches = Regex.Matches(content, pattern.Pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    foreach (Match match in matches)
                    {
                        var lineNumber = content[..match.Index].Count(c => c == '\n') + 1;
                        var lineContent = lineNumber <= lines.Length ? lines[lineNumber - 1].Trim() : "";

                        // Skip comments
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
            catch { /* Skip unreadable files */ }
        }

        foreach (var file in htmlFiles)
        {
            fileIndex++;
            try
            {
                var content = File.ReadAllText(file);
                var relativePath = Path.GetRelativePath(appPath, file);
                var lines = content.Split('\n');

                foreach (var pattern in htmlSecurityPatterns)
                {
                    var matches = Regex.Matches(content, pattern.Pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    foreach (Match match in matches)
                    {
                        var lineNumber = content[..match.Index].Count(c => c == '\n') + 1;
                        var lineContent = lineNumber <= lines.Length ? lines[lineNumber - 1].Trim() : "";

                        // Skip HTML comments
                        if (lineContent.TrimStart().StartsWith("<!--"))
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
            catch { /* Skip unreadable files */ }
        }

        // Check for security-related configuration
        CheckSecurityConfig(appPath, findings);

        Console.WriteLine($"   Scanned {fileIndex}/{totalFiles} file(s)");
        return findings;
    }

    static void CheckSecurityConfig(string appPath, List<Finding> findings)
    {
        // Check for CSP in index.html
        var indexHtmlPath = FindFile(appPath, "index.html");
        if (indexHtmlPath is not null)
        {
            var content = File.ReadAllText(indexHtmlPath);
            if (!content.Contains("Content-Security-Policy", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new Finding
                {
                    Title = "Missing Content-Security-Policy",
                    Description = "No Content-Security-Policy meta tag found in index.html. CSP helps prevent XSS attacks. Consider adding a CSP header via your server or a meta tag.",
                    Severity = Severity.High,
                    Category = "Security",
                    File = Path.GetRelativePath(appPath, indexHtmlPath)
                });
            }
        }

        // Check environment files for secrets
        var envFiles = GetSourceFiles(appPath, "environment*.ts");
        foreach (var envFile in envFiles)
        {
            try
            {
                var content = File.ReadAllText(envFile);
                var relativePath = Path.GetRelativePath(appPath, envFile);

                if (Regex.IsMatch(content, @"(password|secret|private[_-]?key)\s*[:=]", RegexOptions.IgnoreCase))
                {
                    findings.Add(new Finding
                    {
                        Title = "Secrets in environment file",
                        Description = "Possible secrets detected in environment file. Angular environment files are bundled into the client-side code and visible to users. Use server-side configuration for secrets.",
                        Severity = Severity.Critical,
                        Category = "Security",
                        File = relativePath
                    });
                }
            }
            catch { }
        }

        // Check .gitignore for environment files
        var gitignorePath = Path.Combine(appPath, ".gitignore");
        if (File.Exists(gitignorePath))
        {
            var gitignore = File.ReadAllText(gitignorePath);
            if (!gitignore.Contains("environment", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new Finding
                {
                    Title = "Environment files not in .gitignore",
                    Description = "Environment files may contain API URLs or configuration that should not be committed. Consider adding environment.prod.ts to .gitignore.",
                    Severity = Severity.Medium,
                    Category = "Security",
                    File = ".gitignore"
                });
            }
        }

        // Check for package-lock.json (dependency integrity)
        if (!File.Exists(Path.Combine(appPath, "package-lock.json")) &&
            !File.Exists(Path.Combine(appPath, "yarn.lock")) &&
            !File.Exists(Path.Combine(appPath, "pnpm-lock.yaml")))
        {
            findings.Add(new Finding
            {
                Title = "No lock file found",
                Description = "No package-lock.json, yarn.lock, or pnpm-lock.yaml found. Lock files ensure reproducible builds and prevent supply chain attacks.",
                Severity = Severity.High,
                Category = "Security"
            });
        }
    }

    // ───────────────────────────────────────────────────────────
    // Step 3: Storage Analysis
    // ───────────────────────────────────────────────────────────

    static List<Finding> AnalyzeStorage(string appPath)
    {
        var findings = new List<Finding>();
        var tsFiles = GetSourceFiles(appPath, "*.ts");

        var storagePatterns = new List<SecurityPattern>
        {
            new(@"localStorage\.setItem\s*\(", "localStorage.setItem", "Data stored in localStorage is accessible to any JavaScript on the page and persists indefinitely. Never store tokens or sensitive data in localStorage.", Severity.High, "Storage"),
            new(@"localStorage\.getItem\s*\(", "localStorage.getItem", "Reading from localStorage. If this retrieves authentication tokens, consider using HttpOnly cookies instead.", Severity.Medium, "Storage"),
            new(@"sessionStorage\.setItem\s*\(", "sessionStorage.setItem", "Data in sessionStorage is accessible to page JavaScript. Prefer HttpOnly cookies for auth tokens.", Severity.Medium, "Storage"),
            new(@"sessionStorage\.getItem\s*\(", "sessionStorage.getItem", "Reading from sessionStorage. Ensure no sensitive data is stored.", Severity.Low, "Storage"),
            new(@"document\.cookie\s*=", "Direct cookie write", "Setting cookies via document.cookie. Use a cookie service and ensure Secure, HttpOnly, and SameSite flags.", Severity.High, "Storage"),
            new(@"IndexedDB|indexedDB\.open", "IndexedDB usage", "IndexedDB stores data client-side. Ensure no sensitive/PII data is stored unencrypted.", Severity.Medium, "Storage"),
            new(@"\.setItem\s*\(\s*['""](?:token|auth|jwt|access_token|refresh_token|session)", "Auth token in storage", "Authentication token being stored in browser storage. Tokens in localStorage/sessionStorage are vulnerable to XSS. Use HttpOnly cookies.", Severity.Critical, "Storage"),
            new(@"\.getItem\s*\(\s*['""](?:token|auth|jwt|access_token|refresh_token|session)", "Auth token read from storage", "Reading auth token from browser storage. Consider migrating to HttpOnly cookie-based authentication.", Severity.High, "Storage"),
            new(@"(localStorage|sessionStorage)\s*\.\s*clear\s*\(\s*\)", "Storage clear on logout", "Storage clear detected. Ensure this is part of a complete logout flow that also invalidates server-side sessions.", Severity.Low, "Storage"),
        };

        foreach (var file in tsFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                var relativePath = Path.GetRelativePath(appPath, file);
                var lines = content.Split('\n');

                foreach (var pattern in storagePatterns)
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

        return findings;
    }

    // ───────────────────────────────────────────────────────────
    // Step 4: API Communication Analysis
    // ───────────────────────────────────────────────────────────

    static List<Finding> AnalyzeApiCommunication(string appPath)
    {
        var findings = new List<Finding>();
        var tsFiles = GetSourceFiles(appPath, "*.ts");

        // Check for HttpInterceptor usage
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

                // Detect interceptors
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

                // Detect retry logic
                if (Regex.IsMatch(content, @"(retry|retryWhen|RetryConfig)", RegexOptions.IgnoreCase))
                    hasRetryLogic = true;

                // Check patterns
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

        // Add findings based on missing patterns
        if (!hasInterceptor)
        {
            findings.Add(new Finding
            {
                Title = "No HTTP Interceptor found",
                Description = "No HttpInterceptor implementation detected. Interceptors are essential for centralized auth token injection, error handling, logging, and retry logic.",
                Severity = Severity.High,
                Category = "API Communication"
            });
        }

        if (hasInterceptor && !hasAuthInterceptor)
        {
            findings.Add(new Finding
            {
                Title = "No auth interceptor detected",
                Description = "HTTP interceptors exist but none appear to handle authentication headers. Implement an auth interceptor for consistent token management.",
                Severity = Severity.High,
                Category = "API Communication"
            });
        }

        if (hasInterceptor && !hasErrorInterceptor)
        {
            findings.Add(new Finding
            {
                Title = "No error interceptor detected",
                Description = "No centralized HTTP error handling interceptor found. Implement an error interceptor to handle 401/403 redirects and user-facing error messages.",
                Severity = Severity.Medium,
                Category = "API Communication"
            });
        }

        if (!hasCsrfProtection)
        {
            findings.Add(new Finding
            {
                Title = "No CSRF/XSRF protection detected",
                Description = "No CSRF token handling detected. Angular's HttpClient supports XSRF protection via HttpClientXsrfModule. Ensure your server and client coordinate CSRF protection.",
                Severity = Severity.High,
                Category = "API Communication"
            });
        }

        if (!hasRetryLogic)
        {
            findings.Add(new Finding
            {
                Title = "No HTTP retry logic detected",
                Description = "No retry logic found for HTTP calls. Consider implementing retry with exponential backoff for transient failures using RxJS retry/retryWhen operators.",
                Severity = Severity.Low,
                Category = "API Communication"
            });
        }

        return findings;
    }

    // ───────────────────────────────────────────────────────────
    // Step 5: Application Design Analysis
    // ───────────────────────────────────────────────────────────

    static List<Finding> AnalyzeDesign(string appPath)
    {
        var findings = new List<Finding>();
        var tsFiles = GetSourceFiles(appPath, "*.ts");

        // Check for lazy loading
        var hasLazyLoading = false;
        var hasRouteGuards = false;
        var hasEnvironments = false;
        var hasStandaloneComponents = false;
        var hasOnPushStrategy = false;
        var hasTrackBy = false;
        var hasUnsubscribe = false;
        var hasServiceWorker = false;
        var totalComponents = 0;
        var componentsWithOnPush = 0;
        var hasStrictMode = false;

        // Check tsconfig for strict mode
        var tsconfigPath = Path.Combine(appPath, "tsconfig.json");
        if (File.Exists(tsconfigPath))
        {
            try
            {
                var tsconfig = File.ReadAllText(tsconfigPath);
                if (Regex.IsMatch(tsconfig, @"""strict""\s*:\s*true"))
                    hasStrictMode = true;
            }
            catch { }
        }

        if (!hasStrictMode)
        {
            findings.Add(new Finding
            {
                Title = "TypeScript strict mode not enabled",
                Description = "strict mode is not enabled in tsconfig.json. Enable \"strict\": true to catch more type errors at compile time and improve code quality.",
                Severity = Severity.Medium,
                Category = "Application Design",
                File = "tsconfig.json"
            });
        }

        // Check environment files
        var envDir = Path.Combine(appPath, "src", "environments");
        if (Directory.Exists(envDir))
        {
            var envFiles = Directory.GetFiles(envDir, "*.ts");
            hasEnvironments = envFiles.Length >= 2;
        }

        if (!hasEnvironments)
        {
            findings.Add(new Finding
            {
                Title = "Missing environment configuration",
                Description = "Less than 2 environment files found. Ensure you have separate environment.ts and environment.prod.ts for development and production settings.",
                Severity = Severity.Medium,
                Category = "Application Design"
            });
        }

        // Check for ngsw-config.json (service worker)
        if (File.Exists(Path.Combine(appPath, "ngsw-config.json")))
            hasServiceWorker = true;

        var htmlFiles = GetSourceFiles(appPath, "*.html");

        foreach (var file in htmlFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                if (Regex.IsMatch(content, @"trackBy\s*:"))
                    hasTrackBy = true;
            }
            catch { }
        }

        foreach (var file in tsFiles)
        {
            try
            {
                var content = File.ReadAllText(file);

                // Lazy loading
                if (Regex.IsMatch(content, @"loadChildren\s*:|loadComponent\s*:", RegexOptions.IgnoreCase))
                    hasLazyLoading = true;

                // Route guards
                if (Regex.IsMatch(content, @"(canActivate|canDeactivate|canLoad|CanActivateFn|CanMatchFn)", RegexOptions.IgnoreCase))
                    hasRouteGuards = true;

                // Standalone components
                if (Regex.IsMatch(content, @"standalone\s*:\s*true"))
                    hasStandaloneComponents = true;

                // OnPush change detection
                if (Regex.IsMatch(content, @"@Component\s*\("))
                {
                    totalComponents++;
                    if (Regex.IsMatch(content, @"changeDetection\s*:\s*ChangeDetectionStrategy\.OnPush"))
                        componentsWithOnPush++;
                }

                // Unsubscribe patterns
                if (Regex.IsMatch(content, @"(unsubscribe|takeUntil|DestroyRef|takeUntilDestroyed|AsyncPipe)", RegexOptions.IgnoreCase))
                    hasUnsubscribe = true;
            }
            catch { }
        }

        if (!hasLazyLoading)
        {
            findings.Add(new Finding
            {
                Title = "No lazy loading detected",
                Description = "No lazy-loaded routes found (loadChildren/loadComponent). Lazy loading reduces initial bundle size significantly. Split feature modules into lazy-loaded routes.",
                Severity = Severity.High,
                Category = "Application Design"
            });
        }

        if (!hasRouteGuards)
        {
            findings.Add(new Finding
            {
                Title = "No route guards detected",
                Description = "No route guards (canActivate, canLoad, etc.) found. Route guards are essential for protecting authenticated routes and preventing unauthorized access.",
                Severity = Severity.High,
                Category = "Application Design"
            });
        }

        if (totalComponents > 0 && componentsWithOnPush < totalComponents / 2)
        {
            findings.Add(new Finding
            {
                Title = "Low OnPush change detection adoption",
                Description = $"Only {componentsWithOnPush}/{totalComponents} components use OnPush change detection. OnPush significantly improves performance by reducing change detection cycles.",
                Severity = Severity.Medium,
                Category = "Application Design"
            });
        }

        if (!hasUnsubscribe)
        {
            findings.Add(new Finding
            {
                Title = "No subscription cleanup patterns found",
                Description = "No takeUntil, unsubscribe, takeUntilDestroyed, or AsyncPipe patterns detected. Memory leaks from unmanaged subscriptions are a common Angular issue.",
                Severity = Severity.High,
                Category = "Application Design"
            });
        }

        if (!hasTrackBy)
        {
            findings.Add(new Finding
            {
                Title = "No trackBy usage in templates",
                Description = "No trackBy functions found in *ngFor directives. trackBy prevents unnecessary DOM re-renders and improves list rendering performance.",
                Severity = Severity.Medium,
                Category = "Application Design"
            });
        }

        // Browser resource usage patterns
        var browserResourcePatterns = new List<SecurityPattern>
        {
            new(@"setTimeout\s*\(", "setTimeout usage", "setTimeout detected. Ensure timers are cleared in ngOnDestroy to prevent memory leaks. Consider RxJS timer() instead.", Severity.Medium, "Application Design"),
            new(@"setInterval\s*\(", "setInterval usage", "setInterval detected. Intervals MUST be cleared in ngOnDestroy. Prefer RxJS interval() with takeUntil for automatic cleanup.", Severity.High, "Application Design"),
            new(@"(?<!remove)addEventListener\s*\(", "Manual event listener", "Manual addEventListener bypasses Angular zone and won't auto-clean. Use @HostListener or Renderer2, and remove in ngOnDestroy.", Severity.Medium, "Application Design"),
            new(@"new\s+WebSocket\s*\(", "WebSocket connection", "Raw WebSocket usage. Consider RxJS webSocket() for automatic reconnection and cleanup, or a library like socket.io-client.", Severity.Medium, "Application Design"),
            new(@"new\s+(MutationObserver|ResizeObserver|IntersectionObserver)", "DOM Observer", "DOM Observer detected. Call disconnect() in ngOnDestroy to prevent memory leaks.", Severity.Medium, "Application Design"),
            new(@"document\.(getElementById|querySelector|querySelectorAll|getElementsBy)", "Direct DOM query", "Direct DOM access bypasses Angular rendering. Use @ViewChild, Renderer2, or Angular directives. Breaks SSR.", Severity.High, "Application Design"),
            new(@"\.nativeElement", "nativeElement access", "Direct nativeElement access breaks Angular abstraction and server-side rendering. Use Renderer2 for DOM manipulation.", Severity.Medium, "Application Design"),
            new(@"window\.location\s*=|window\.location\.href\s*=", "window.location navigation", "Direct window.location navigation bypasses Angular Router lifecycle hooks. Use Router.navigate().", Severity.Medium, "Application Design"),
            new(@"new\s+Worker\s*\(", "Web Worker", "Web Worker detected. Good for offloading heavy computation from the main thread.", Severity.Info, "Application Design"),
            new(@"navigator\.(geolocation|mediaDevices|bluetooth|usb|serial)", "Browser hardware API", "Browser hardware/sensor API access. Handle permission denial gracefully and check API availability.", Severity.Low, "Application Design"),
            new(@"requestAnimationFrame\s*\(", "requestAnimationFrame", "requestAnimationFrame detected. Cancel with cancelAnimationFrame in ngOnDestroy to prevent leaks.", Severity.Medium, "Application Design"),
        };

        foreach (var file in tsFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                var relativePath = Path.GetRelativePath(appPath, file);
                var lines = content.Split('\n');

                foreach (var pattern in browserResourcePatterns)
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

        // Check for unit test files
        var specFiles = GetSourceFiles(appPath, "*.spec.ts");
        if (specFiles.Count == 0)
        {
            findings.Add(new Finding
            {
                Title = "No unit tests found",
                Description = "No .spec.ts test files found. Unit tests are critical for code quality, catching regressions, and safe refactoring.",
                Severity = Severity.High,
                Category = "Application Design"
            });
        }
        else if (totalComponents > 0 && specFiles.Count < totalComponents * 0.5)
        {
            findings.Add(new Finding
            {
                Title = "Low test coverage",
                Description = $"Only {specFiles.Count} spec files for {totalComponents} components. Aim for at least one spec file per component/service.",
                Severity = Severity.Medium,
                Category = "Application Design"
            });
        }

        return findings;
    }

    // ───────────────────────────────────────────────────────────
    // Step 6: Library Health Analysis
    // ───────────────────────────────────────────────────────────

    static List<Finding> AnalyzeLibraries(ProjectMetadata metadata)
    {
        var findings = new List<Finding>();

        // Angular version analysis
        if (metadata.AngularVersion is not null)
        {
            var majorVersion = 0;
            var versionParts = metadata.AngularVersion.Split('.');
            if (versionParts.Length > 0)
                int.TryParse(versionParts[0], out majorVersion);

            if (majorVersion > 0 && majorVersion < 16)
            {
                findings.Add(new Finding
                {
                    Title = $"Angular {majorVersion} is end-of-life",
                    Description = $"Angular {metadata.AngularVersion} is no longer receiving security patches. Upgrade to a supported LTS version (Angular 16+ recommended, 18+ ideal). See https://angular.dev/reference/releases",
                    Severity = Severity.Critical,
                    Category = "Libraries"
                });
            }
            else if (majorVersion >= 16 && majorVersion < 18)
            {
                findings.Add(new Finding
                {
                    Title = $"Angular {majorVersion} approaching end-of-support",
                    Description = $"Angular {metadata.AngularVersion} is in LTS or approaching end-of-life. Plan an upgrade to Angular 18+ for continued support and new features (signals, control flow, etc.).",
                    Severity = Severity.Medium,
                    Category = "Libraries"
                });
            }
        }

        // Check for known vulnerable / deprecated packages
        var knownIssues = new Dictionary<string, (string message, Severity severity)>(StringComparer.OrdinalIgnoreCase)
        {
            ["protractor"] = ("Protractor is deprecated. Migrate to Cypress, Playwright, or Angular's built-in e2e support.", Severity.High),
            ["tslint"] = ("TSLint is deprecated since 2019. Migrate to ESLint with @angular-eslint.", Severity.High),
            ["codelyzer"] = ("Codelyzer (TSLint rules for Angular) is deprecated. Use @angular-eslint.", Severity.Medium),
            ["@angular/http"] = ("@angular/http is removed. Use @angular/common/http (HttpClient) instead.", Severity.Critical),
            ["rxjs-compat"] = ("rxjs-compat is a migration shim for RxJS 5→6. Remove it and update to RxJS 6+ syntax.", Severity.Medium),
            ["node-sass"] = ("node-sass is deprecated. Switch to sass (Dart Sass) which is actively maintained.", Severity.High),
            ["@ngrx/store-devtools"] = ("@ngrx/store-devtools should only be in devDependencies, not dependencies.", Severity.Medium),
            ["jquery"] = ("jQuery usage in Angular is an anti-pattern. Use Angular's built-in DOM manipulation and event handling.", Severity.Medium),
            ["moment"] = ("Moment.js is in maintenance mode and adds ~300KB to bundle. Use date-fns, Luxon, or native Date/Intl.", Severity.Medium),
            ["lodash"] = ("Consider lodash-es for tree-shakeable imports, or use native JS methods where possible to reduce bundle size.", Severity.Low),
            ["angular-in-memory-web-api"] = ("in-memory-web-api should only be in devDependencies. Remove from production builds.", Severity.Medium),
            ["@angular/flex-layout"] = ("@angular/flex-layout is deprecated. Migrate to CSS Flexbox/Grid or TailwindCSS.", Severity.High),
            ["angularfire2"] = ("angularfire2 is renamed to @angular/fire. Update the package name.", Severity.Medium),
            ["core-js"] = ("core-js polyfills may be unnecessary for modern browsers. Angular CLI handles polyfills automatically.", Severity.Low),
            ["classlist.js"] = ("classlist.js polyfill is no longer needed for modern browsers.", Severity.Low),
            ["web-animations-js"] = ("web-animations-js polyfill is no longer needed. Modern browsers support Web Animations API natively.", Severity.Low),
            ["@angular-devkit/build-angular"] = ("", Severity.Info), // Skip, standard
        };

        foreach (var dep in metadata.Dependencies.Concat(metadata.DevDependencies))
        {
            if (knownIssues.TryGetValue(dep.Key, out var issue) && !string.IsNullOrEmpty(issue.message))
            {
                findings.Add(new Finding
                {
                    Title = $"{dep.Key} ({dep.Value})",
                    Description = issue.message,
                    Severity = issue.severity,
                    Category = "Libraries"
                });
            }
        }

        // Check for missing recommended packages
        if (!metadata.Dependencies.ContainsKey("@angular/cdk") &&
            !metadata.Dependencies.ContainsKey("@angular/material"))
        {
            // Not a finding, just info
        }

        if (!metadata.DevDependencies.ContainsKey("eslint") &&
            !metadata.DevDependencies.ContainsKey("@angular-eslint/builder"))
        {
            findings.Add(new Finding
            {
                Title = "No linter configured",
                Description = "Neither ESLint nor @angular-eslint found. A linter catches code quality issues and enforces consistent coding standards.",
                Severity = Severity.Medium,
                Category = "Libraries"
            });
        }

        // Check TypeScript version
        if (metadata.DevDependencies.TryGetValue("typescript", out var tsVersion))
        {
            var cleanVersion = tsVersion.TrimStart('^', '~');
            var parts = cleanVersion.Split('.');
            if (parts.Length > 0 && int.TryParse(parts[0], out var tsMajor) && tsMajor < 5)
            {
                findings.Add(new Finding
                {
                    Title = $"TypeScript {cleanVersion} is outdated",
                    Description = "TypeScript 5.x brings significant performance improvements and new features. Upgrade TypeScript alongside your Angular version.",
                    Severity = Severity.Medium,
                    Category = "Libraries"
                });
            }
        }

        // Check RxJS version
        if (metadata.Dependencies.TryGetValue("rxjs", out var rxjsVersion))
        {
            var cleanVersion = rxjsVersion.TrimStart('^', '~');
            var parts = cleanVersion.Split('.');
            if (parts.Length > 0 && int.TryParse(parts[0], out var rxMajor) && rxMajor < 7)
            {
                findings.Add(new Finding
                {
                    Title = $"RxJS {cleanVersion} is outdated",
                    Description = "RxJS 7+ provides smaller bundle size, better TypeScript types, and improved performance. Upgrade to RxJS 7.x.",
                    Severity = Severity.Medium,
                    Category = "Libraries"
                });
            }
        }

        return findings;
    }

    // ───────────────────────────────────────────────────────────
    // API Endpoint Extraction
    // ───────────────────────────────────────────────────────────

    static List<ApiEndpointInfo> ExtractApiEndpoints(string appPath)
    {
        var endpoints = new List<ApiEndpointInfo>();
        var tsFiles = GetSourceFiles(appPath, "*.ts");

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

                var matches = Regex.Matches(content, httpCallPattern, RegexOptions.Singleline);
                foreach (Match match in matches)
                {
                    var lineNumber = content[..match.Index].Count(c => c == '\n') + 1;
                    var lineContent = lineNumber <= lines.Length ? lines[lineNumber - 1].Trim() : "";
                    if (lineContent.TrimStart().StartsWith("//")) continue;

                    endpoints.Add(new ApiEndpointInfo
                    {
                        HttpMethod = match.Groups[1].Value.ToUpper(),
                        Url = match.Groups[3].Value,
                        File = relativePath,
                        Line = lineNumber,
                        ServiceName = serviceName
                    });
                }

                // Also check for fetch() calls
                var fetchMatches = Regex.Matches(content, @"fetch\s*\(\s*([`'""])(.*?)\1", RegexOptions.Singleline);
                foreach (Match match in fetchMatches)
                {
                    var lineNumber = content[..match.Index].Count(c => c == '\n') + 1;
                    var lineContent = lineNumber <= lines.Length ? lines[lineNumber - 1].Trim() : "";
                    if (lineContent.TrimStart().StartsWith("//")) continue;

                    endpoints.Add(new ApiEndpointInfo
                    {
                        HttpMethod = "FETCH",
                        Url = match.Groups[2].Value,
                        File = relativePath,
                        Line = lineNumber,
                        ServiceName = serviceName
                    });
                }
            }
            catch { }
        }

        return endpoints;
    }

    // ───────────────────────────────────────────────────────────
    // Library Version Checking (npm outdated)
    // ───────────────────────────────────────────────────────────

    static List<LibraryVersionInfo> CheckLibraryVersions(string appPath)
    {
        var versions = new List<LibraryVersionInfo>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "npm",
                Arguments = "outdated --json",
                WorkingDirectory = appPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                Console.WriteLine("   \u26a0\ufe0f  npm not found. Skipping version check.");
                return versions;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);

            if (!string.IsNullOrWhiteSpace(output))
            {
                using var doc = JsonDocument.Parse(output);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var info = new LibraryVersionInfo { Name = prop.Name };

                    if (prop.Value.TryGetProperty("current", out var current))
                        info.Current = current.GetString() ?? "";
                    if (prop.Value.TryGetProperty("wanted", out var wanted))
                        info.Wanted = wanted.GetString() ?? "";
                    if (prop.Value.TryGetProperty("latest", out var latest))
                        info.Latest = latest.GetString() ?? "";

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

    static string DetermineUpdateType(string current, string latest)
    {
        if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(latest)) return "unknown";

        var curParts = current.TrimStart('^', '~').Split('.');
        var latParts = latest.TrimStart('^', '~').Split('.');

        if (curParts.Length > 0 && latParts.Length > 0 && curParts[0] != latParts[0])
            return "major";
        if (curParts.Length > 1 && latParts.Length > 1 && curParts[1] != latParts[1])
            return "minor";
        if (curParts.Length > 2 && latParts.Length > 2 && curParts[2] != latParts[2])
            return "patch";

        return "current";
    }

    // ───────────────────────────────────────────────────────────
    // Security Posture Analysis
    // ───────────────────────────────────────────────────────────

    static List<SecurityPostureItem> BuildSecurityPosture(string appPath, AnalysisReport report)
    {
        var posture = new List<SecurityPostureItem>();
        var tsFiles = GetSourceFiles(appPath, "*.ts");

        bool FileContains(string pattern) =>
            tsFiles.Any(f => { try { return Regex.IsMatch(File.ReadAllText(f), pattern, RegexOptions.IgnoreCase); } catch { return false; } });

        bool FileContainsBoth(string p1, string p2) =>
            tsFiles.Any(f => { try { var c = File.ReadAllText(f); return Regex.IsMatch(c, p1, RegexOptions.IgnoreCase) && Regex.IsMatch(c, p2, RegexOptions.IgnoreCase); } catch { return false; } });

        // Authentication
        var hasAuthGuard = FileContains(@"canActivate|CanActivateFn|canMatch|CanMatchFn");
        posture.Add(new SecurityPostureItem { Area = "Authentication", Check = "Route guards (canActivate)", InPlace = hasAuthGuard, Details = hasAuthGuard ? "Route guards detected protecting routes" : "No route guards found \u2014 authenticated routes are unprotected" });

        var hasAuthInterceptor = FileContainsBoth(@"implements\s+HttpInterceptor|HttpInterceptorFn", @"(authorization|bearer|auth[_-]?token|x-auth)");
        posture.Add(new SecurityPostureItem { Area = "Authentication", Check = "Auth token interceptor", InPlace = hasAuthInterceptor, Details = hasAuthInterceptor ? "HTTP interceptor handles auth tokens" : "No auth interceptor \u2014 tokens must be manually added to each request" });

        var hasRoleCheck = FileContains(@"(role|permission|isAdmin|hasRole|canAccess)");
        posture.Add(new SecurityPostureItem { Area = "Authentication", Check = "Role/permission checks", InPlace = hasRoleCheck, Details = hasRoleCheck ? "Role-based access patterns detected" : "No role-based access control patterns detected" });

        // XSS Protection
        var indexHtml = FindFile(appPath, "index.html");
        var hasCsp = indexHtml is not null && File.ReadAllText(indexHtml).Contains("Content-Security-Policy", StringComparison.OrdinalIgnoreCase);
        posture.Add(new SecurityPostureItem { Area = "XSS Protection", Check = "Content Security Policy", InPlace = hasCsp, Details = hasCsp ? "CSP header/meta tag found in index.html" : "No CSP configured \u2014 XSS attack surface is larger" });

        var hasSanitization = FileContains(@"DomSanitizer");
        posture.Add(new SecurityPostureItem { Area = "XSS Protection", Check = "DomSanitizer usage", InPlace = hasSanitization, Details = hasSanitization ? "DomSanitizer is used for safe HTML/URL handling" : "No DomSanitizer usage detected" });

        // CSRF
        var hasCsrf = FileContains(@"(X-XSRF-TOKEN|X-CSRF-TOKEN|HttpClientXsrfModule|withXsrfConfiguration|xsrfHeaderName)");
        posture.Add(new SecurityPostureItem { Area = "CSRF Protection", Check = "CSRF/XSRF token handling", InPlace = hasCsrf, Details = hasCsrf ? "CSRF token handling detected" : "No CSRF protection \u2014 vulnerable to cross-site request forgery" });

        // Error Handling
        var hasErrorInterceptor = FileContainsBoth(@"implements\s+HttpInterceptor|HttpInterceptorFn", @"(catchError|HttpErrorResponse)");
        posture.Add(new SecurityPostureItem { Area = "Error Handling", Check = "HTTP error interceptor", InPlace = hasErrorInterceptor, Details = hasErrorInterceptor ? "Centralized HTTP error handling detected" : "No error interceptor \u2014 errors may leak server details" });

        var hasGlobalErrorHandler = FileContains(@"implements\s+ErrorHandler");
        posture.Add(new SecurityPostureItem { Area = "Error Handling", Check = "Global ErrorHandler", InPlace = hasGlobalErrorHandler, Details = hasGlobalErrorHandler ? "Custom ErrorHandler implementation found" : "Using default ErrorHandler \u2014 unhandled errors logged to console" });

        // Input Validation
        var hasReactiveForms = FileContains(@"(FormBuilder|FormGroup|FormControl|Validators\.)");
        posture.Add(new SecurityPostureItem { Area = "Input Validation", Check = "Reactive forms validation", InPlace = hasReactiveForms, Details = hasReactiveForms ? "Reactive forms with validators detected" : "No reactive form validation found" });

        // Supply Chain
        var hasLockFile = File.Exists(Path.Combine(appPath, "package-lock.json")) || File.Exists(Path.Combine(appPath, "yarn.lock")) || File.Exists(Path.Combine(appPath, "pnpm-lock.yaml"));
        posture.Add(new SecurityPostureItem { Area = "Supply Chain", Check = "Dependency lock file", InPlace = hasLockFile, Details = hasLockFile ? "Lock file ensures reproducible builds" : "No lock file \u2014 vulnerable to supply chain attacks" });

        // Configuration
        var envDir = Path.Combine(appPath, "src", "environments");
        var hasEnvSep = Directory.Exists(envDir) && Directory.GetFiles(envDir, "*.ts").Length >= 2;
        posture.Add(new SecurityPostureItem { Area = "Configuration", Check = "Environment separation", InPlace = hasEnvSep, Details = hasEnvSep ? "Separate environment files for dev/prod" : "Missing environment separation" });

        // Code Quality
        var tsconfigPath = Path.Combine(appPath, "tsconfig.json");
        var hasStrict = File.Exists(tsconfigPath) && Regex.IsMatch(File.ReadAllText(tsconfigPath), @"""strict""\s*:\s*true");
        posture.Add(new SecurityPostureItem { Area = "Code Quality", Check = "TypeScript strict mode", InPlace = hasStrict, Details = hasStrict ? "Strict type checking enabled" : "Strict mode disabled \u2014 weaker type safety" });

        var hasLinter = report.ProjectMetadata.DevDependencies.ContainsKey("eslint") || report.ProjectMetadata.DevDependencies.ContainsKey("@angular-eslint/builder");
        posture.Add(new SecurityPostureItem { Area = "Code Quality", Check = "Linter configured", InPlace = hasLinter, Details = hasLinter ? "ESLint/angular-eslint configured" : "No linter \u2014 no automated code quality checks" });

        // Transport Security
        var hasInsecureUrls = tsFiles.Any(f => { try { return Regex.IsMatch(File.ReadAllText(f), @"['""`]http://(?!localhost)"); } catch { return false; } });
        posture.Add(new SecurityPostureItem { Area = "Transport Security", Check = "HTTPS-only API calls", InPlace = !hasInsecureUrls, Details = !hasInsecureUrls ? "All detected API URLs use HTTPS (or localhost)" : "HTTP (non-HTTPS) API URLs detected" });

        // Subscription Management
        var hasCleanup = FileContains(@"(takeUntil|takeUntilDestroyed|unsubscribe|DestroyRef|AsyncPipe)");
        posture.Add(new SecurityPostureItem { Area = "Resource Management", Check = "Subscription cleanup", InPlace = hasCleanup, Details = hasCleanup ? "Subscription cleanup patterns detected" : "No subscription cleanup \u2014 potential memory leaks" });

        return posture;
    }

    // ───────────────────────────────────────────────────────────
    // Console Summary
    // ───────────────────────────────────────────────────────────

    static void DisplayConsoleSummary(AnalysisReport report)
    {
        Console.WriteLine(new string('=', 60));
        Console.WriteLine("📊 ANALYSIS SUMMARY");
        Console.WriteLine(new string('=', 60));

        var allFindings = report.AllFindings.ToList();
        var bySeverity = allFindings.GroupBy(f => f.Severity).OrderByDescending(g => g.Key);

        Console.WriteLine($"\n   Total findings: {allFindings.Count}");

        foreach (var group in bySeverity)
        {
            var icon = group.Key switch
            {
                Severity.Critical => "🔴",
                Severity.High => "🟠",
                Severity.Medium => "🟡",
                Severity.Low => "🟢",
                _ => "ℹ️"
            };
            var color = group.Key switch
            {
                Severity.Critical => ConsoleColor.Red,
                Severity.High => ConsoleColor.DarkYellow,
                Severity.Medium => ConsoleColor.Yellow,
                Severity.Low => ConsoleColor.Green,
                _ => ConsoleColor.Gray
            };
            Console.ForegroundColor = color;
            Console.WriteLine($"   {icon} {group.Key}: {group.Count()}");
            Console.ResetColor();
        }

        var byCategory = allFindings.GroupBy(f => f.Category).OrderBy(g => g.Key);
        Console.WriteLine();
        foreach (var group in byCategory)
        {
            Console.WriteLine($"   📋 {group.Key}: {group.Count()} finding(s)");
        }

        // Show top critical findings
        var criticalFindings = allFindings.Where(f => f.Severity is Severity.Critical or Severity.High).Take(10).ToList();
        if (criticalFindings.Count > 0)
        {
            Console.WriteLine($"\n   🔥 Top Critical/High Findings:");
            foreach (var f in criticalFindings)
            {
                var icon = f.Severity == Severity.Critical ? "🔴" : "🟠";
                Console.WriteLine($"      {icon} [{f.Category}] {f.Title}");
                if (!string.IsNullOrEmpty(f.File))
                    Console.WriteLine($"         📄 {f.File}{(f.Line > 0 ? $":{f.Line}" : "")}");
            }
        }

        // Security posture
        if (report.SecurityPosture.Count > 0)
        {
            Console.WriteLine($"\n   🔐 Security Posture:");
            foreach (var item in report.SecurityPosture)
            {
                var icon = item.InPlace ? "✅" : "❌";
                Console.ForegroundColor = item.InPlace ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine($"      {icon} [{item.Area}] {item.Check}");
                Console.ResetColor();
            }
        }

        // API endpoints
        if (report.ApiEndpoints.Count > 0)
        {
            Console.WriteLine($"\n   🌐 Discovered API Endpoints ({report.ApiEndpoints.Count}):");
            foreach (var ep in report.ApiEndpoints.GroupBy(e => e.Url).Take(15))
            {
                var methods = string.Join("/", ep.Select(e => e.HttpMethod).Distinct());
                Console.WriteLine($"      {methods,-8} {ep.Key}");
            }
            if (report.ApiEndpoints.GroupBy(e => e.Url).Count() > 15)
                Console.WriteLine($"      ... and {report.ApiEndpoints.GroupBy(e => e.Url).Count() - 15} more");
        }

        // Library versions
        if (report.LibraryVersions.Count > 0)
        {
            var majors = report.LibraryVersions.Where(v => v.UpdateType == "major").ToList();
            if (majors.Count > 0)
            {
                Console.WriteLine($"\n   🔄 Major Version Updates Available ({majors.Count}):");
                foreach (var v in majors.Take(10))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write($"      {v.Name}");
                    Console.ResetColor();
                    Console.WriteLine($"  {v.Current} → {v.Latest}");
                }
            }
        }
    }

    // ───────────────────────────────────────────────────────────
    // HTML Report Generation
    // ───────────────────────────────────────────────────────────

    static void GenerateHtmlReport(AnalysisReport report, string outputFile)
    {
        var html = new StringBuilder();
        var allFindings = report.AllFindings.ToList();
        var criticalCount = allFindings.Count(f => f.Severity == Severity.Critical);
        var highCount = allFindings.Count(f => f.Severity == Severity.High);
        var mediumCount = allFindings.Count(f => f.Severity == Severity.Medium);
        var lowCount = allFindings.Count(f => f.Severity == Severity.Low);
        var infoCount = allFindings.Count(f => f.Severity == Severity.Info);

        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang='en'>");
        html.AppendLine("<head>");
        html.AppendLine("  <meta charset='UTF-8'>");
        html.AppendLine("  <meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        html.AppendLine($"  <title>Angular Security &amp; Health Report — {report.ProjectMetadata.Name}</title>");
        html.AppendLine("  <style>");
        html.AppendLine("    :root { --critical:#e74c3c; --high:#e67e22; --medium:#f1c40f; --low:#27ae60; --info:#3498db; }");
        html.AppendLine("    * { box-sizing:border-box; margin:0; padding:0; }");
        html.AppendLine("    body { font-family:'Segoe UI',Tahoma,sans-serif; background:#f0f2f5; color:#2c3e50; padding:20px; }");
        html.AppendLine("    .container { max-width:1400px; margin:0 auto; }");
        html.AppendLine("    .header { background:linear-gradient(135deg,#dd1b16 0%,#c3002f 100%); color:#fff; padding:30px; border-radius:12px; margin-bottom:24px; }");
        html.AppendLine("    .header h1 { font-size:28px; margin-bottom:8px; }");
        html.AppendLine("    .header-meta { opacity:.85; font-size:14px; }");
        html.AppendLine("    .summary-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(160px,1fr)); gap:16px; margin-bottom:24px; }");
        html.AppendLine("    .summary-card { background:#fff; border-radius:10px; padding:20px; text-align:center; box-shadow:0 2px 8px rgba(0,0,0,.08); border-top:4px solid var(--info); }");
        html.AppendLine("    .summary-card.critical { border-top-color:var(--critical); }");
        html.AppendLine("    .summary-card.high { border-top-color:var(--high); }");
        html.AppendLine("    .summary-card.medium { border-top-color:var(--medium); }");
        html.AppendLine("    .summary-card.low { border-top-color:var(--low); }");
        html.AppendLine("    .summary-card .number { font-size:42px; font-weight:700; }");
        html.AppendLine("    .summary-card.critical .number { color:var(--critical); }");
        html.AppendLine("    .summary-card.high .number { color:var(--high); }");
        html.AppendLine("    .summary-card.medium .number { color:var(--medium); }");
        html.AppendLine("    .summary-card.low .number { color:var(--low); }");
        html.AppendLine("    .summary-card .label { font-size:13px; color:#7f8c8d; margin-top:4px; }");
        html.AppendLine("    .section { background:#fff; border-radius:10px; box-shadow:0 2px 8px rgba(0,0,0,.08); margin-bottom:24px; overflow:hidden; }");
        html.AppendLine("    .section-header { padding:18px 24px; cursor:pointer; display:flex; justify-content:space-between; align-items:center; }");
        html.AppendLine("    .section-header:hover { background:#f8f9fa; }");
        html.AppendLine("    .section-header h2 { font-size:20px; }");
        html.AppendLine("    .section-badge { display:inline-block; padding:3px 12px; border-radius:20px; font-size:12px; font-weight:600; color:#fff; margin-left:10px; }");
        html.AppendLine("    .badge-critical { background:var(--critical); } .badge-high { background:var(--high); } .badge-medium { background:var(--medium); } .badge-low { background:var(--low); } .badge-info { background:var(--info); }");
        html.AppendLine("    .section-content { display:none; padding:0 24px 24px; }");
        html.AppendLine("    .section-content.show { display:block; }");
        html.AppendLine("    .finding { border:1px solid #e9ecef; border-radius:8px; margin-bottom:12px; overflow:hidden; }");
        html.AppendLine("    .finding-header { padding:14px 18px; display:flex; justify-content:space-between; align-items:center; cursor:pointer; }");
        html.AppendLine("    .finding-header:hover { background:#f8f9fa; }");
        html.AppendLine("    .finding.sev-critical { border-left:4px solid var(--critical); }");
        html.AppendLine("    .finding.sev-high { border-left:4px solid var(--high); }");
        html.AppendLine("    .finding.sev-medium { border-left:4px solid var(--medium); }");
        html.AppendLine("    .finding.sev-low { border-left:4px solid var(--low); }");
        html.AppendLine("    .finding.sev-info { border-left:4px solid var(--info); }");
        html.AppendLine("    .finding-title { font-weight:600; font-size:15px; }");
        html.AppendLine("    .finding-detail { display:none; padding:0 18px 14px; font-size:14px; color:#555; }");
        html.AppendLine("    .finding-detail.show { display:block; }");
        html.AppendLine("    .finding-meta { font-size:12px; color:#95a5a6; margin-top:6px; }");
        html.AppendLine("    .code-snippet { background:#2d2d2d; color:#f8f8f2; padding:10px 14px; border-radius:6px; font-family:'Consolas',monospace; font-size:13px; overflow-x:auto; margin-top:8px; white-space:pre; }");
        html.AppendLine("    .toggle { font-size:18px; transition:transform .2s; color:#95a5a6; }");
        html.AppendLine("    .toggle.open { transform:rotate(90deg); }");
        html.AppendLine("    .filter-bar { display:flex; gap:8px; flex-wrap:wrap; margin-bottom:16px; }");
        html.AppendLine("    .filter-btn { padding:6px 16px; border:2px solid #ddd; border-radius:20px; background:#fff; cursor:pointer; font-size:13px; font-weight:500; }");
        html.AppendLine("    .filter-btn.active { border-color:var(--info); background:#ebf5fb; color:var(--info); }");
        html.AppendLine("    .deps-table { width:100%; border-collapse:collapse; }");
        html.AppendLine("    .deps-table th { background:#34495e; color:#fff; padding:10px 14px; text-align:left; font-size:13px; }");
        html.AppendLine("    .deps-table td { padding:8px 14px; border-bottom:1px solid #ecf0f1; font-size:13px; }");
        html.AppendLine("    .deps-table tr:hover { background:#f8f9fa; }");
        html.AppendLine("    .posture-grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(340px,1fr)); gap:12px; }");
        html.AppendLine("    .posture-item { display:flex; align-items:center; gap:12px; padding:14px; background:#fff; border-radius:8px; border:1px solid #e9ecef; }");
        html.AppendLine("    .posture-item.pass { border-left:4px solid var(--low); }");
        html.AppendLine("    .posture-item.fail { border-left:4px solid var(--critical); }");
        html.AppendLine("    .posture-icon { font-size:22px; }");
        html.AppendLine("    .posture-check { font-weight:600; font-size:14px; }");
        html.AppendLine("    .posture-area { font-size:11px; color:#95a5a6; text-transform:uppercase; letter-spacing:.5px; }");
        html.AppendLine("    .posture-detail { font-size:12px; color:#7f8c8d; margin-top:2px; }");
        html.AppendLine("    .endpoint-method { display:inline-block; padding:2px 8px; border-radius:4px; font-size:11px; font-weight:700; color:#fff; min-width:55px; text-align:center; }");
        html.AppendLine("    .method-GET { background:#27ae60; } .method-POST { background:#2980b9; } .method-PUT { background:#e67e22; } .method-DELETE { background:#e74c3c; } .method-PATCH { background:#8e44ad; } .method-FETCH { background:#7f8c8d; }");
        html.AppendLine("    .version-badge { display:inline-block; padding:2px 10px; border-radius:10px; font-size:11px; font-weight:600; }");
        html.AppendLine("    .version-major { background:#fde8e8; color:#c0392b; } .version-minor { background:#fff3cd; color:#856404; } .version-patch { background:#d4edda; color:#155724; }");
        html.AppendLine("  </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("  <div class='container'>");

        // Header
        html.AppendLine("    <div class='header'>");
        html.AppendLine($"      <h1>🛡️ Angular Security &amp; Health Report</h1>");
        html.AppendLine($"      <div class='header-meta'>{report.ProjectMetadata.Name} · Angular {report.ProjectMetadata.AngularVersion ?? "unknown"} · {report.Timestamp:yyyy-MM-dd HH:mm}</div>");
        html.AppendLine("    </div>");

        // Summary cards
        html.AppendLine("    <div class='summary-grid'>");
        html.AppendLine($"      <div class='summary-card critical'><div class='number'>{criticalCount}</div><div class='label'>Critical</div></div>");
        html.AppendLine($"      <div class='summary-card high'><div class='number'>{highCount}</div><div class='label'>High</div></div>");
        html.AppendLine($"      <div class='summary-card medium'><div class='number'>{mediumCount}</div><div class='label'>Medium</div></div>");
        html.AppendLine($"      <div class='summary-card low'><div class='number'>{lowCount}</div><div class='label'>Low</div></div>");
        html.AppendLine($"      <div class='summary-card'><div class='number'>{allFindings.Count}</div><div class='label'>Total</div></div>");
        html.AppendLine("    </div>");

        // Security Posture Scorecard
        if (report.SecurityPosture.Count > 0)
        {
            var passCount = report.SecurityPosture.Count(p => p.InPlace);
            var failCount = report.SecurityPosture.Count(p => !p.InPlace);
            html.AppendLine("    <div class='section'>");
            html.AppendLine("      <div class='section-header' onclick='toggleSection(\"posture\")'>");
            html.AppendLine("        <div><h2>\ud83d\udd10 Security Posture</h2></div>");
            html.AppendLine($"        <div><span class='section-badge badge-low'>{passCount} in place</span><span class='section-badge badge-critical'>{failCount} missing</span><span class='toggle' id='toggle-posture'>\u25b6</span></div>");
            html.AppendLine("      </div>");
            html.AppendLine("      <div class='section-content show' id='content-posture'>");
            html.AppendLine("        <div class='posture-grid'>");
            foreach (var item in report.SecurityPosture)
            {
                var cls = item.InPlace ? "pass" : "fail";
                var icon = item.InPlace ? "\u2705" : "\u274c";
                html.AppendLine($"          <div class='posture-item {cls}'>");
                html.AppendLine($"            <div class='posture-icon'>{icon}</div>");
                html.AppendLine($"            <div><div class='posture-area'>{Escape(item.Area)}</div><div class='posture-check'>{Escape(item.Check)}</div><div class='posture-detail'>{Escape(item.Details)}</div></div>");
                html.AppendLine($"          </div>");
            }
            html.AppendLine("        </div>");
            html.AppendLine("      </div>");
            html.AppendLine("    </div>");
        }

        // API Endpoints
        if (report.ApiEndpoints.Count > 0)
        {
            html.AppendLine("    <div class='section'>");
            html.AppendLine("      <div class='section-header' onclick='toggleSection(\"endpoints\")'>");
            html.AppendLine("        <div><h2>\ud83c\udf10 Discovered API Endpoints</h2></div>");
            html.AppendLine($"        <div><span class='section-badge badge-info'>{report.ApiEndpoints.Count} call(s)</span><span class='toggle' id='toggle-endpoints'>\u25b6</span></div>");
            html.AppendLine("      </div>");
            html.AppendLine("      <div class='section-content' id='content-endpoints'>");
            html.AppendLine("        <table class='deps-table'><thead><tr><th>Method</th><th>URL / Pattern</th><th>Service</th><th>File</th><th>Line</th></tr></thead><tbody>");
            foreach (var ep in report.ApiEndpoints.OrderBy(e => e.Url).ThenBy(e => e.HttpMethod))
            {
                var methodClass = $"method-{ep.HttpMethod}";
                html.AppendLine($"          <tr><td><span class='endpoint-method {methodClass}'>{Escape(ep.HttpMethod)}</span></td><td><code>{Escape(ep.Url)}</code></td><td>{Escape(ep.ServiceName)}</td><td>{Escape(ep.File ?? "")}</td><td>{ep.Line}</td></tr>");
            }
            html.AppendLine("        </tbody></table>");
            html.AppendLine("      </div>");
            html.AppendLine("    </div>");
        }

        // Library Versions (npm outdated)
        if (report.LibraryVersions.Count > 0)
        {
            var majorCount = report.LibraryVersions.Count(v => v.UpdateType == "major");
            html.AppendLine("    <div class='section'>");
            html.AppendLine("      <div class='section-header' onclick='toggleSection(\"versions\")'>");
            html.AppendLine("        <div><h2>\ud83d\udd04 Library Versions (npm outdated)</h2></div>");
            html.AppendLine($"        <div>");
            if (majorCount > 0) html.AppendLine($"          <span class='section-badge badge-critical'>{majorCount} major</span>");
            html.AppendLine($"          <span class='section-badge badge-info'>{report.LibraryVersions.Count} outdated</span><span class='toggle' id='toggle-versions'>\u25b6</span></div>");
            html.AppendLine("      </div>");
            html.AppendLine("      <div class='section-content' id='content-versions'>");
            html.AppendLine("        <table class='deps-table'><thead><tr><th>Package</th><th>Current</th><th>Wanted</th><th>Latest</th><th>Update</th></tr></thead><tbody>");
            foreach (var v in report.LibraryVersions.OrderByDescending(v => v.UpdateType == "major").ThenBy(v => v.Name))
            {
                var badgeClass = v.UpdateType switch { "major" => "version-major", "minor" => "version-minor", _ => "version-patch" };
                html.AppendLine($"          <tr><td><strong>{Escape(v.Name)}</strong></td><td>{Escape(v.Current)}</td><td>{Escape(v.Wanted)}</td><td>{Escape(v.Latest)}</td><td><span class='version-badge {badgeClass}'>{Escape(v.UpdateType)}</span></td></tr>");
            }
            html.AppendLine("        </tbody></table>");
            html.AppendLine("      </div>");
            html.AppendLine("    </div>");
        }

        // Category sections
        var categories = new[] { "Security", "Storage", "API Communication", "Application Design", "Libraries" };
        foreach (var category in categories)
        {
            var catFindings = allFindings.Where(f => f.Category == category).OrderByDescending(f => f.Severity).ToList();
            if (catFindings.Count == 0) continue;

            var catId = category.Replace(" ", "-").ToLower();
            var catCritical = catFindings.Count(f => f.Severity == Severity.Critical);
            var catHigh = catFindings.Count(f => f.Severity == Severity.High);
            var catIcon = category switch
            {
                "Security" => "🛡️",
                "Storage" => "💾",
                "API Communication" => "🌐",
                "Application Design" => "🏗️",
                "Libraries" => "📚",
                _ => "📋"
            };

            html.AppendLine($"    <div class='section'>");
            html.AppendLine($"      <div class='section-header' onclick='toggleSection(\"{catId}\")'>");
            html.AppendLine($"        <div><h2>{catIcon} {category}</h2></div>");
            html.AppendLine($"        <div>");
            if (catCritical > 0) html.AppendLine($"          <span class='section-badge badge-critical'>{catCritical} critical</span>");
            if (catHigh > 0) html.AppendLine($"          <span class='section-badge badge-high'>{catHigh} high</span>");
            html.AppendLine($"          <span class='section-badge badge-info'>{catFindings.Count} total</span>");
            html.AppendLine($"          <span class='toggle' id='toggle-{catId}'>▶</span>");
            html.AppendLine($"        </div>");
            html.AppendLine($"      </div>");
            html.AppendLine($"      <div class='section-content' id='content-{catId}'>");

            var findingIdx = 0;
            foreach (var f in catFindings)
            {
                findingIdx++;
                var fId = $"{catId}-f{findingIdx}";
                var sevClass = $"sev-{f.Severity.ToString().ToLower()}";
                var sevBadge = $"badge-{f.Severity.ToString().ToLower()}";

                html.AppendLine($"        <div class='finding {sevClass}'>");
                html.AppendLine($"          <div class='finding-header' onclick='toggleFinding(\"{fId}\")'>");
                html.AppendLine($"            <div class='finding-title'><span class='section-badge {sevBadge}'>{f.Severity}</span> {Escape(f.Title)}</div>");
                html.AppendLine($"            <span class='toggle' id='toggle-{fId}'>▶</span>");
                html.AppendLine($"          </div>");
                html.AppendLine($"          <div class='finding-detail' id='detail-{fId}'>");
                html.AppendLine($"            <p>{Escape(f.Description)}</p>");
                if (!string.IsNullOrEmpty(f.File))
                    html.AppendLine($"            <div class='finding-meta'>📄 {Escape(f.File)}{(f.Line > 0 ? $":{f.Line}" : "")}</div>");
                if (!string.IsNullOrEmpty(f.CodeSnippet))
                    html.AppendLine($"            <div class='code-snippet'>{Escape(f.CodeSnippet)}</div>");
                html.AppendLine($"          </div>");
                html.AppendLine($"        </div>");
            }

            html.AppendLine($"      </div>");
            html.AppendLine($"    </div>");
        }

        // Dependencies table
        html.AppendLine("    <div class='section'>");
        html.AppendLine("      <div class='section-header' onclick='toggleSection(\"deps\")'>");
        html.AppendLine("        <div><h2>📦 Dependencies</h2></div>");
        html.AppendLine($"        <div><span class='section-badge badge-info'>{report.ProjectMetadata.Dependencies.Count + report.ProjectMetadata.DevDependencies.Count} packages</span><span class='toggle' id='toggle-deps'>▶</span></div>");
        html.AppendLine("      </div>");
        html.AppendLine("      <div class='section-content' id='content-deps'>");
        html.AppendLine("        <h3 style='margin-bottom:12px;'>Production Dependencies</h3>");
        html.AppendLine("        <table class='deps-table'><thead><tr><th>Package</th><th>Version</th></tr></thead><tbody>");
        foreach (var dep in report.ProjectMetadata.Dependencies.OrderBy(d => d.Key))
            html.AppendLine($"          <tr><td>{Escape(dep.Key)}</td><td>{Escape(dep.Value)}</td></tr>");
        html.AppendLine("        </tbody></table>");

        html.AppendLine("        <h3 style='margin:20px 0 12px;'>Dev Dependencies</h3>");
        html.AppendLine("        <table class='deps-table'><thead><tr><th>Package</th><th>Version</th></tr></thead><tbody>");
        foreach (var dep in report.ProjectMetadata.DevDependencies.OrderBy(d => d.Key))
            html.AppendLine($"          <tr><td>{Escape(dep.Key)}</td><td>{Escape(dep.Value)}</td></tr>");
        html.AppendLine("        </tbody></table>");
        html.AppendLine("      </div>");
        html.AppendLine("    </div>");

        // JavaScript
        html.AppendLine("  </div>");
        html.AppendLine("  <script>");
        html.AppendLine("    function toggleSection(id){");
        html.AppendLine("      var c=document.getElementById('content-'+id),t=document.getElementById('toggle-'+id);");
        html.AppendLine("      c.classList.toggle('show');t.classList.toggle('open');");
        html.AppendLine("    }");
        html.AppendLine("    function toggleFinding(id){");
        html.AppendLine("      var d=document.getElementById('detail-'+id),t=document.getElementById('toggle-'+id);");
        html.AppendLine("      d.classList.toggle('show');t.classList.toggle('open');");
        html.AppendLine("    }");
        html.AppendLine("  </script>");
        html.AppendLine("</body></html>");

        File.WriteAllText(outputFile, html.ToString());
    }

    static string Escape(string text) =>
        text?.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;") ?? "";

    // ───────────────────────────────────────────────────────────
    // Utilities
    // ───────────────────────────────────────────────────────────

    static List<string> GetSourceFiles(string appPath, string pattern)
    {
        var srcDir = Path.Combine(appPath, "src");
        var searchDir = Directory.Exists(srcDir) ? srcDir : appPath;

        try
        {
            return Directory.GetFiles(searchDir, pattern, SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}.angular{Path.DirectorySeparatorChar}"))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    static string? FindFile(string appPath, string fileName)
    {
        var srcPath = Path.Combine(appPath, "src", fileName);
        if (File.Exists(srcPath)) return srcPath;

        var rootPath = Path.Combine(appPath, fileName);
        if (File.Exists(rootPath)) return rootPath;

        try
        {
            var files = Directory.GetFiles(Path.Combine(appPath, "src"), fileName, SearchOption.AllDirectories);
            return files.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    // ───────────────────────────────────────────────────────────
    // Argument Parsing
    // ───────────────────────────────────────────────────────────

    static ParsedArguments? ParseArguments(string[] args)
    {
        string? appPath = null;
        string? outputFile = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--path" or "-p":
                    if (i + 1 < args.Length) appPath = args[++i];
                    break;
                case "--output" or "-o":
                    if (i + 1 < args.Length) outputFile = args[++i];
                    break;
            }
        }

        if (string.IsNullOrEmpty(appPath)) return null;

        return new ParsedArguments { AppPath = appPath, OutputFile = outputFile };
    }

    static void ShowUsage()
    {
        Console.WriteLine("Angular App Analyser — Security & Health Scanner");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  AngularAppAnalyser --path <angular-app-folder> [--output <report.html>]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --path <path>       Path to the Angular application root (containing package.json)");
        Console.WriteLine("  -o, --output <file>     Path to output HTML report (optional)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  AngularAppAnalyser -p ./my-angular-app");
        Console.WriteLine("  AngularAppAnalyser --path C:\\Projects\\MyApp --output report.html");
    }

    // ───────────────────────────────────────────────────────────
    // Models
    // ───────────────────────────────────────────────────────────

    class ParsedArguments
    {
        public required string AppPath { get; set; }
        public string? OutputFile { get; set; }
    }

    class AnalysisReport
    {
        public required string AppPath { get; set; }
        public DateTime Timestamp { get; set; }
        public ProjectMetadata ProjectMetadata { get; set; } = new();
        public List<Finding> SecurityFindings { get; set; } = [];
        public List<Finding> StorageFindings { get; set; } = [];
        public List<Finding> ApiFindings { get; set; } = [];
        public List<Finding> DesignFindings { get; set; } = [];
        public List<Finding> LibraryFindings { get; set; } = [];
        public List<LibraryVersionInfo> LibraryVersions { get; set; } = [];
        public List<ApiEndpointInfo> ApiEndpoints { get; set; } = [];
        public List<SecurityPostureItem> SecurityPosture { get; set; } = [];

        public IEnumerable<Finding> AllFindings =>
            SecurityFindings
                .Concat(StorageFindings)
                .Concat(ApiFindings)
                .Concat(DesignFindings)
                .Concat(LibraryFindings);
    }

    class ProjectMetadata
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string? AngularVersion { get; set; }
        public Dictionary<string, string> Dependencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> DevDependencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> AngularProjects { get; set; } = [];
        public bool HasBudgets { get; set; }
        public bool HasTsConfig { get; set; }
        public bool HasTsConfigApp { get; set; }
    }

    class Finding
    {
        public required string Title { get; set; }
        public required string Description { get; set; }
        public Severity Severity { get; set; }
        public string Category { get; set; } = "";
        public string? File { get; set; }
        public int Line { get; set; }
        public string? CodeSnippet { get; set; }
    }

    enum Severity
    {
        Info = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    record SecurityPattern(string Pattern, string Title, string Description, Severity Severity, string Category);

    class LibraryVersionInfo
    {
        public string Name { get; set; } = "";
        public string Current { get; set; } = "";
        public string Wanted { get; set; } = "";
        public string Latest { get; set; } = "";
        public string UpdateType { get; set; } = "unknown";
    }

    class ApiEndpointInfo
    {
        public string HttpMethod { get; set; } = "";
        public string Url { get; set; } = "";
        public string? File { get; set; }
        public int Line { get; set; }
        public string ServiceName { get; set; } = "";
    }

    class SecurityPostureItem
    {
        public string Area { get; set; } = "";
        public string Check { get; set; } = "";
        public bool InPlace { get; set; }
        public string Details { get; set; } = "";
    }
}
