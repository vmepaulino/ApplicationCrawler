using System.Text.RegularExpressions;
using AngularAppAnalyser.Abstractions;
using AngularAppAnalyser.Models;

namespace AngularAppAnalyser.Analyzers;

internal static class DesignAnalyzer
{
    public static List<Finding> Analyze(AnalysisContext ctx)
    {
        var findings = new List<Finding>();
        var appPath = ctx.AppPath;
        var tsFiles = ctx.GetSourceFiles("*.ts");

        var hasLazyLoading = false;
        var hasRouteGuards = false;
        var hasEnvironments = false;
        var hasTrackBy = false;
        var hasUnsubscribe = false;
        var totalComponents = 0;
        var componentsWithOnPush = 0;
        var hasStrictMode = false;

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
            findings.Add(new Finding { Title = "TypeScript strict mode not enabled", Description = "strict mode is not enabled in tsconfig.json. Enable \"strict\": true to catch more type errors at compile time.", Severity = Severity.Medium, Category = "Application Design", File = "tsconfig.json" });

        var envDir = Path.Combine(appPath, "src", "environments");
        if (Directory.Exists(envDir))
            hasEnvironments = Directory.GetFiles(envDir, "*.ts").Length >= 2;

        if (!hasEnvironments)
            findings.Add(new Finding { Title = "Missing environment configuration", Description = "Less than 2 environment files found. Ensure you have separate environment.ts and environment.prod.ts.", Severity = Severity.Medium, Category = "Application Design" });

        var htmlFiles = ctx.GetSourceFiles("*.html");

        foreach (var file in htmlFiles)
        {
            try
            {
                if (Regex.IsMatch(File.ReadAllText(file), @"trackBy\s*:"))
                    hasTrackBy = true;
            }
            catch { }
        }

        foreach (var file in tsFiles)
        {
            try
            {
                var content = File.ReadAllText(file);

                if (Regex.IsMatch(content, @"loadChildren\s*:|loadComponent\s*:", RegexOptions.IgnoreCase))
                    hasLazyLoading = true;
                if (Regex.IsMatch(content, @"(canActivate|canDeactivate|canLoad|CanActivateFn|CanMatchFn)", RegexOptions.IgnoreCase))
                    hasRouteGuards = true;
                if (Regex.IsMatch(content, @"@Component\s*\("))
                {
                    totalComponents++;
                    if (Regex.IsMatch(content, @"changeDetection\s*:\s*ChangeDetectionStrategy\.OnPush"))
                        componentsWithOnPush++;
                }
                if (Regex.IsMatch(content, @"(unsubscribe|takeUntil|DestroyRef|takeUntilDestroyed|AsyncPipe)", RegexOptions.IgnoreCase))
                    hasUnsubscribe = true;
            }
            catch { }
        }

        if (!hasLazyLoading)
            findings.Add(new Finding { Title = "No lazy loading detected", Description = "No lazy-loaded routes found (loadChildren/loadComponent). Lazy loading reduces initial bundle size significantly.", Severity = Severity.High, Category = "Application Design" });
        if (!hasRouteGuards)
            findings.Add(new Finding { Title = "No route guards detected", Description = "No route guards (canActivate, canLoad, etc.) found. Route guards are essential for protecting authenticated routes.", Severity = Severity.High, Category = "Application Design" });
        if (totalComponents > 0 && componentsWithOnPush < totalComponents / 2)
            findings.Add(new Finding { Title = "Low OnPush change detection adoption", Description = $"Only {componentsWithOnPush}/{totalComponents} components use OnPush change detection.", Severity = Severity.Medium, Category = "Application Design" });
        if (!hasUnsubscribe)
            findings.Add(new Finding { Title = "No subscription cleanup patterns found", Description = "No takeUntil, unsubscribe, takeUntilDestroyed, or AsyncPipe patterns detected.", Severity = Severity.High, Category = "Application Design" });
        if (!hasTrackBy)
            findings.Add(new Finding { Title = "No trackBy usage in templates", Description = "No trackBy functions found in *ngFor directives. trackBy prevents unnecessary DOM re-renders.", Severity = Severity.Medium, Category = "Application Design" });

        // Browser resource patterns
        var browserPatterns = new List<SecurityPattern>
        {
            new(@"setTimeout\s*\(", "setTimeout usage", "setTimeout detected. Ensure timers are cleared in ngOnDestroy. Consider RxJS timer() instead.", Severity.Medium, "Application Design"),
            new(@"setInterval\s*\(", "setInterval usage", "setInterval detected. Intervals MUST be cleared in ngOnDestroy. Prefer RxJS interval() with takeUntil.", Severity.High, "Application Design"),
            new(@"(?<!remove)addEventListener\s*\(", "Manual event listener", "Manual addEventListener bypasses Angular zone. Use @HostListener or Renderer2, and remove in ngOnDestroy.", Severity.Medium, "Application Design"),
            new(@"new\s+WebSocket\s*\(", "WebSocket connection", "Raw WebSocket usage. Consider RxJS webSocket() for automatic reconnection and cleanup.", Severity.Medium, "Application Design"),
            new(@"new\s+(MutationObserver|ResizeObserver|IntersectionObserver)", "DOM Observer", "DOM Observer detected. Call disconnect() in ngOnDestroy to prevent memory leaks.", Severity.Medium, "Application Design"),
            new(@"document\.(getElementById|querySelector|querySelectorAll|getElementsBy)", "Direct DOM query", "Direct DOM access bypasses Angular rendering. Use @ViewChild, Renderer2, or Angular directives.", Severity.High, "Application Design"),
            new(@"\.nativeElement", "nativeElement access", "Direct nativeElement access breaks Angular abstraction and SSR. Use Renderer2.", Severity.Medium, "Application Design"),
            new(@"window\.location\s*=|window\.location\.href\s*=", "window.location navigation", "Direct window.location bypasses Angular Router lifecycle hooks. Use Router.navigate().", Severity.Medium, "Application Design"),
            new(@"new\s+Worker\s*\(", "Web Worker", "Web Worker detected. Good for offloading heavy computation.", Severity.Info, "Application Design"),
            new(@"navigator\.(geolocation|mediaDevices|bluetooth|usb|serial)", "Browser hardware API", "Browser hardware API access. Handle permission denial gracefully.", Severity.Low, "Application Design"),
            new(@"requestAnimationFrame\s*\(", "requestAnimationFrame", "requestAnimationFrame detected. Cancel in ngOnDestroy to prevent leaks.", Severity.Medium, "Application Design"),
        };

        findings.AddRange(AnalysisContext.ScanFiles(tsFiles, appPath, browserPatterns));

        var specFiles = ctx.GetSourceFiles("*.spec.ts");
        if (specFiles.Count == 0)
            findings.Add(new Finding { Title = "No unit tests found", Description = "No .spec.ts test files found. Unit tests are critical for code quality.", Severity = Severity.High, Category = "Application Design" });
        else if (totalComponents > 0 && specFiles.Count < totalComponents * 0.5)
            findings.Add(new Finding { Title = "Low test coverage", Description = $"Only {specFiles.Count} spec files for {totalComponents} components.", Severity = Severity.Medium, Category = "Application Design" });

        return findings;
    }
}
