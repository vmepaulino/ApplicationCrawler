using System.Text.RegularExpressions;
using AngularAppAnalyser.Abstractions;
using AngularAppAnalyser.Models;

namespace AngularAppAnalyser.Analyzers;

internal static class AppStructureAnalyzer
{
    public static List<FunctionalArea> Analyze(AnalysisContext ctx)
    {
        var areas = new List<FunctionalArea>();
        var appPath = ctx.AppPath;

        var appDir = ctx.ResolveAppDirectory();
        if (appDir is null)
        {
            Console.WriteLine("   \u26a0\ufe0f  app directory not found (checked app/ and src/app/)");
            return areas;
        }

        Console.WriteLine($"   Using app directory: {Path.GetRelativePath(appPath, appDir)}/");

        // Root-level — only files directly in app/ (not nested)
        var rootTs = GetFilteredFiles(appDir, "*.ts", SearchOption.TopDirectoryOnly);
        var rootHtml = GetFilteredFiles(appDir, "*.html", SearchOption.TopDirectoryOnly);
        {
            var rootArea = BuildArea("(root)", appDir, rootTs, rootHtml, appPath);
            if (rootArea.TsFileCount > 0 || rootArea.HtmlFileCount > 0)
                areas.Add(rootArea);
        }

        // Sub-folders = functional areas — recursive search inside each
        var subDirs = Directory.GetDirectories(appDir);
        Console.WriteLine($"   Found {subDirs.Length} folder(s) in {Path.GetRelativePath(appPath, appDir)}/");

        foreach (var dir in subDirs)
        {
            var dirName = Path.GetFileName(dir);
            if (dirName.StartsWith(".")) continue;

            var tsFiles = GetFilteredFiles(dir, "*.ts", SearchOption.AllDirectories);
            var htmlFiles = GetFilteredFiles(dir, "*.html", SearchOption.AllDirectories);
            var area = BuildArea(dirName, dir, tsFiles, htmlFiles, appPath);

            areas.Add(area);
            var avgScore = area.Components.Count > 0
                ? (area.Components.Average(c => c.TsScore) + area.Components.Average(c => c.HtmlScore)) / 2.0 : 0;
            Console.WriteLine($"   \ud83d\udcc1 {dirName}: {area.TsFileCount} .ts, {area.HtmlFileCount} .html, {area.ComponentCount} comp (avg {avgScore:F1}/5), {area.Services.Count} svc, {area.GuardCount} guard, {area.ModuleCount} mod");
            if (area.ScannedFolders.Count > 0)
                Console.WriteLine($"      \u2514 sub-folders: {string.Join(", ", area.ScannedFolders)}");
        }

        return areas;
    }

    private static List<string> GetFilteredFiles(string directory, string pattern, SearchOption searchOption)
    {
        try
        {
            return Directory.GetFiles(directory, pattern, searchOption)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")
                         && !f.EndsWith(".spec.ts"))
                .ToList();
        }
        catch { return []; }
    }

    private static FunctionalArea BuildArea(string name, string dir, List<string> tsFiles, List<string> htmlFiles, string appPath)
    {
        // Discover nested sub-folders that contain files
        var nestedFolders = tsFiles.Concat(htmlFiles)
            .Select(f => Path.GetDirectoryName(f)!)
            .Distinct()
            .Where(d => !string.Equals(d, dir, StringComparison.OrdinalIgnoreCase))
            .Select(d => Path.GetRelativePath(dir, d))
            .OrderBy(d => d)
            .ToList();

        // Detect components by filename convention AND content (@Component decorator).
        // Supports both conventions:
        //   Angular CLI default: countries.component.ts
        //   PascalCase:          CountriesComponent.ts
        var componentFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in tsFiles)
        {
            if (f.EndsWith("Component.ts") || IsComponentByName(f))
            {
                componentFiles.Add(f);
                continue;
            }

            // Skip files that clearly aren't components
            if (f.EndsWith(".service.ts") || f.EndsWith(".module.ts") || f.EndsWith(".directive.ts") ||
                f.EndsWith(".pipe.ts") || f.EndsWith(".guard.ts") || f.EndsWith(".interceptor.ts") ||
                f.EndsWith(".model.ts") || f.EndsWith(".interface.ts") || f.EndsWith(".enum.ts") ||
                f.EndsWith(".spec.ts") || f.EndsWith(".routes.ts") || f.EndsWith(".config.ts"))
                continue;

            try
            {
                var content = File.ReadAllText(f);
                if (Regex.IsMatch(content, @"@Component\s*\("))
                    componentFiles.Add(f);
            }
            catch { }
        }

        var area = new FunctionalArea
        {
            Name = name,
            Path = Path.GetRelativePath(appPath, dir),
            TsFileCount = tsFiles.Count,
            HtmlFileCount = htmlFiles.Count,
            ComponentCount = componentFiles.Count,
            DirectiveCount = tsFiles.Count(f => f.EndsWith(".directive.ts")),
            PipeCount = tsFiles.Count(f => f.EndsWith(".pipe.ts")),
            GuardCount = tsFiles.Count(f => f.EndsWith(".guard.ts")),
            InterceptorCount = tsFiles.Count(f => f.EndsWith(".interceptor.ts")),
            ModuleCount = tsFiles.Count(f => f.EndsWith(".module.ts")),
            ScannedFolders = nestedFolders,
        };

        foreach (var sf in tsFiles.Where(f => f.EndsWith(".service.ts")))
            area.Services.Add(AnalyzeService(sf, appPath));
        foreach (var cf in componentFiles)
            area.Components.Add(AnalyzeComponent(cf, appPath));

        return area;
    }

    /// <summary>
    /// Fast-path: detect PascalCase component files like CountriesComponent.ts.
    /// Matches filenames ending in "Component.ts" where the char before "Component" is uppercase.
    /// </summary>
    private static bool IsComponentByName(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return fileName.Length > "Component".Length
            && fileName.EndsWith("Component", StringComparison.Ordinal)
            && char.IsUpper(fileName[0]);
    }

    /// <summary>
    /// Derive a clean display name by stripping the "Component" suffix.
    /// Handles both conventions:
    ///   countries.component.ts → countries
    ///   CountriesComponent.ts  → Countries
    /// </summary>
    private static string DeriveComponentDisplayName(string tsFilePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(tsFilePath);

        if (fileName.EndsWith(".component", StringComparison.OrdinalIgnoreCase))
            return fileName[..^".component".Length];

        if (fileName.EndsWith("Component", StringComparison.Ordinal) && fileName.Length > "Component".Length)
            return fileName[..^"Component".Length];

        return fileName;
    }

    // ── Component Analysis ─────────────────────────────────────

    public static ComponentAnalysis AnalyzeComponent(string tsFilePath, string appPath)
    {
        var baseName = tsFilePath[..^".ts".Length];
        var htmlPath = baseName + ".html";

        var displayName = DeriveComponentDisplayName(tsFilePath);

        // Look for matching HTML: same-name.html, or via templateUrl in the TS
        string? resolvedHtmlFile = null;
        if (File.Exists(htmlPath))
        {
            resolvedHtmlFile = Path.GetRelativePath(appPath, htmlPath);
        }
        else
        {
            // Try reading templateUrl from the TS file
            try
            {
                var peek = File.ReadAllText(tsFilePath);
                var templateUrlMatch = Regex.Match(peek, @"templateUrl\s*:\s*['""]([^'""]+)['""]");
                if (templateUrlMatch.Success)
                {
                    var templateRelative = templateUrlMatch.Groups[1].Value;
                    var templateFull = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(tsFilePath)!, templateRelative));
                    if (File.Exists(templateFull))
                        resolvedHtmlFile = Path.GetRelativePath(appPath, templateFull);
                }
            }
            catch { }
        }

        var comp = new ComponentAnalysis
        {
            Name = displayName,
            TsFile = Path.GetRelativePath(appPath, tsFilePath),
            HtmlFile = resolvedHtmlFile
        };

        try
        {
            var tsContent = File.ReadAllText(tsFilePath);

            var ctorMatch = Regex.Match(tsContent, @"constructor\s*\(([\s\S]*?)\)", RegexOptions.Multiline);
            if (ctorMatch.Success)
            {
                foreach (Match inj in Regex.Matches(ctorMatch.Groups[1].Value,
                    @"(?:private|protected|public|readonly)\s+\w+\s*:\s*(\w+)"))
                    comp.Dependencies.Add(inj.Groups[1].Value);
            }

            foreach (Match inj in Regex.Matches(tsContent, @"=\s*inject\s*\(\s*(\w+)\s*\)"))
                comp.Dependencies.Add(inj.Groups[1].Value);

            foreach (Match imp in Regex.Matches(tsContent, @"import\s+\{([^}]+)\}\s+from\s+['""](@angular/[^'""]+|rxjs[^'""]*)['""]"))
            {
                foreach (var m in imp.Groups[1].Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
                    comp.Dependencies.Add(m);
            }

            comp.Dependencies = comp.Dependencies.Distinct().ToList();

            // ── TS Score ──
            var tsPoints = 3.0;

            comp.IsStandalone = Regex.IsMatch(tsContent, @"standalone\s*:\s*true");
            if (comp.IsStandalone) { tsPoints += 0.5; comp.TsModernTraits.Add("standalone component"); }
            else { tsPoints -= 0.5; comp.TsLegacyTraits.Add("not standalone (NgModule-based)"); }

            comp.UsesOnPush = Regex.IsMatch(tsContent, @"changeDetection\s*:\s*ChangeDetectionStrategy\.OnPush");
            if (comp.UsesOnPush) { tsPoints += 0.3; comp.TsModernTraits.Add("OnPush change detection"); }
            else comp.TsLegacyTraits.Add("default change detection");

            if (Regex.IsMatch(tsContent, @"\b(signal|computed|effect)\s*[<(]"))
            { tsPoints += 0.5; comp.TsModernTraits.Add("uses signals"); }

            if (Regex.IsMatch(tsContent, @"\b(input|output|model)\s*[<(]"))
            { tsPoints += 0.5; comp.TsModernTraits.Add("signal-based input/output"); }
            else if (Regex.IsMatch(tsContent, @"@(Input|Output)\s*\("))
            { tsPoints -= 0.3; comp.TsLegacyTraits.Add("decorator-based @Input/@Output"); }

            if (Regex.IsMatch(tsContent, @"=\s*inject\s*\("))
            { tsPoints += 0.3; comp.TsModernTraits.Add("inject() function"); }
            else if (ctorMatch.Success && ctorMatch.Groups[1].Value.Trim().Length > 0)
            { tsPoints -= 0.2; comp.TsLegacyTraits.Add("constructor injection"); }

            if (Regex.IsMatch(tsContent, @"(takeUntilDestroyed|DestroyRef)"))
            { tsPoints += 0.3; comp.TsModernTraits.Add("takeUntilDestroyed/DestroyRef"); }
            else if (Regex.IsMatch(tsContent, @"takeUntil|ngOnDestroy.*unsubscribe"))
            { comp.TsLegacyTraits.Add("manual subscription cleanup"); }
            else if (Regex.IsMatch(tsContent, @"\.subscribe\s*\("))
            { tsPoints -= 0.3; comp.TsLegacyTraits.Add("subscribe without cleanup"); }

            if (Regex.IsMatch(tsContent, @"\bviewChild\s*[<(]"))
            { tsPoints += 0.3; comp.TsModernTraits.Add("viewChild() signal query"); }
            else if (Regex.IsMatch(tsContent, @"@ViewChild\s*\("))
            { comp.TsLegacyTraits.Add("@ViewChild decorator"); }

            var anyCount = Regex.Matches(tsContent, @":\s*any\b").Count;
            if (anyCount > 3) { tsPoints -= 0.3; comp.TsLegacyTraits.Add($"{anyCount}\u00d7 any type"); }

            if (Regex.IsMatch(tsContent, @"ngOnChanges\s*\(\s*\w+\s*:\s*SimpleChanges"))
            { tsPoints -= 0.2; comp.TsLegacyTraits.Add("ngOnChanges/SimpleChanges (use signal inputs)"); }

            comp.TsScore = Math.Clamp((int)Math.Round(tsPoints), 1, 5);

            // ── HTML Score ──
            if (comp.HtmlFile is not null && File.Exists(Path.Combine(appPath, comp.HtmlFile)))
            {
                var htmlContent = File.ReadAllText(Path.Combine(appPath, comp.HtmlFile));
                comp.HtmlScore = ScoreHtml(htmlContent, comp);
            }
            else
            {
                var templateMatch = Regex.Match(tsContent, @"template\s*:\s*`([\s\S]*?)`");
                if (templateMatch.Success)
                {
                    var inlineHtml = templateMatch.Groups[1].Value;
                    var htmlPoints = 3.5;
                    comp.HtmlModernTraits.Add("inline template");
                    if (Regex.IsMatch(inlineHtml, @"@if\s*\(|@for\s*\("))
                    { htmlPoints += 1.0; comp.HtmlModernTraits.Add("new control flow"); }
                    if (Regex.IsMatch(inlineHtml, @"\*ngIf|\*ngFor"))
                    { htmlPoints -= 0.5; comp.HtmlLegacyTraits.Add("legacy structural directives"); }
                    comp.HtmlScore = Math.Clamp((int)Math.Round(htmlPoints), 1, 5);
                }
            }
        }
        catch { comp.TsScore = 0; comp.HtmlScore = 0; }

        return comp;
    }

    private static int ScoreHtml(string htmlContent, ComponentAnalysis comp)
    {
        var htmlPoints = 3.0;

        if (Regex.IsMatch(htmlContent, @"@if\s*\(|@for\s*\(|@switch\s*\("))
        { htmlPoints += 1.0; comp.HtmlModernTraits.Add("new @if/@for/@switch control flow"); }

        var ngIfCount = Regex.Matches(htmlContent, @"\*ngIf\s*=").Count;
        var ngForCount = Regex.Matches(htmlContent, @"\*ngFor\s*=").Count;
        var ngSwitchCount = Regex.Matches(htmlContent, @"\[ngSwitch\]").Count;
        if (ngIfCount + ngForCount + ngSwitchCount > 0)
        {
            htmlPoints -= 0.5;
            comp.HtmlLegacyTraits.Add($"*ngIf({ngIfCount}) *ngFor({ngForCount}) [ngSwitch]({ngSwitchCount}) \u2192 migrate to @if/@for/@switch");
        }

        if (ngForCount > 0 && Regex.IsMatch(htmlContent, @"trackBy\s*:"))
            comp.HtmlModernTraits.Add("trackBy in *ngFor");
        else if (ngForCount > 0)
        { htmlPoints -= 0.3; comp.HtmlLegacyTraits.Add("*ngFor without trackBy"); }

        if (Regex.IsMatch(htmlContent, @"@defer\s*[\({]"))
        { htmlPoints += 0.5; comp.HtmlModernTraits.Add("@defer lazy loading"); }

        var asyncCount = Regex.Matches(htmlContent, @"\|\s*async\b").Count;
        if (asyncCount > 0)
            comp.HtmlModernTraits.Add($"async pipe ({asyncCount}\u00d7)");

        if (Regex.IsMatch(htmlContent, @"\[innerHTML\]\s*="))
        { htmlPoints -= 0.3; comp.HtmlLegacyTraits.Add("[innerHTML] binding"); }

        var lineCount = htmlContent.Split('\n').Length;
        if (lineCount > 200) { htmlPoints -= 0.5; comp.HtmlLegacyTraits.Add($"large template ({lineCount} lines) \u2014 consider splitting"); }
        else if (lineCount > 100) { htmlPoints -= 0.2; comp.HtmlLegacyTraits.Add($"template {lineCount} lines"); }

        var inlineStyles = Regex.Matches(htmlContent, @"style\s*=\s*['""]").Count;
        if (inlineStyles > 3) { htmlPoints -= 0.2; comp.HtmlLegacyTraits.Add($"{inlineStyles} inline styles"); }

        return Math.Clamp((int)Math.Round(htmlPoints), 1, 5);
    }

    // ── Service Analysis ───────────────────────────────────────

    public static ServiceAnalysis AnalyzeService(string filePath, string appPath)
    {
        var analysis = new ServiceAnalysis
        {
            File = Path.GetRelativePath(appPath, filePath),
            Name = Path.GetFileNameWithoutExtension(filePath).Replace(".service", "")
        };

        try
        {
            var content = File.ReadAllText(filePath);
            var lines = content.Split('\n');

            analysis.IsProvidedInRoot = Regex.IsMatch(content, @"providedIn\s*:\s*'root'");

            var ctorMatch = Regex.Match(content, @"constructor\s*\(([\s\S]*?)\)", RegexOptions.Multiline);
            if (ctorMatch.Success)
            {
                foreach (Match inj in Regex.Matches(ctorMatch.Groups[1].Value, @"(?:private|protected|public|readonly)\s+\w+\s*:\s*(\w+)"))
                    analysis.InjectedDependencies.Add(inj.Groups[1].Value);
            }

            analysis.MethodCount = Regex.Matches(content,
                @"^\s*(?:public\s+|async\s+|private\s+|protected\s+)*\w+\s*\([^)]*\)\s*(?::\s*[\w<>\[\]|&\s]+)?\s*\{",
                RegexOptions.Multiline).Count;

            if (!analysis.IsProvidedInRoot && !Regex.IsMatch(content, @"providedIn\s*:"))
                analysis.Issues.Add(new ServiceIssue { Title = "Service not providedIn root", Description = "Missing providedIn: 'root'. May cause multiple instances and prevents tree-shaking.", Severity = Severity.Medium });

            var untypedHttp = Regex.Matches(content, @"this\.\w+\.(get|post|put|delete|patch)\s*\((?!\s*<)");
            if (untypedHttp.Count > 0)
                analysis.Issues.Add(new ServiceIssue { Title = $"Untyped HTTP call(s) ({untypedHttp.Count})", Description = "HTTP calls without generic type. Typed responses enable compile-time checking.", Severity = Severity.Medium, Line = content[..untypedHttp[0].Index].Count(c => c == '\n') + 1 });

            foreach (Match sm in Regex.Matches(content, @"\.subscribe\s*\("))
            {
                var ln = content[..sm.Index].Count(c => c == '\n') + 1;
                var lc = ln <= lines.Length ? lines[ln - 1].Trim() : "";
                if (lc.StartsWith("//")) continue;
                var after = content[(sm.Index + sm.Length)..];
                var snippet = after[..Math.Min(300, after.Length)];
                if (!Regex.IsMatch(snippet, @"(error|err)\s*[=:(]|\berror\b", RegexOptions.IgnoreCase))
                    analysis.Issues.Add(new ServiceIssue { Title = "subscribe() without error handler", Description = "Use { next:, error: } observer pattern or catchError.", Severity = Severity.Medium, Line = ln, CodeSnippet = lc.Length > 120 ? lc[..120] + "..." : lc });
            }

            if (Regex.IsMatch(content, @"\.toPromise\s*\("))
            { var m = Regex.Match(content, @"\.toPromise\s*\("); analysis.Issues.Add(new ServiceIssue { Title = "Deprecated toPromise()", Description = "Use firstValueFrom() or lastValueFrom().", Severity = Severity.Medium, Line = content[..m.Index].Count(c => c == '\n') + 1 }); }

            var anyCount = Regex.Matches(content, @":\s*any\b").Count;
            if (anyCount > 3)
                analysis.Issues.Add(new ServiceIssue { Title = $"Excessive 'any' type ({anyCount}\u00d7)", Description = "Define interfaces for API responses and parameters.", Severity = Severity.Medium });

            if (Regex.IsMatch(content, @"document\.(getElementById|querySelector|querySelectorAll|getElementsBy)"))
            { var m = Regex.Match(content, @"document\.(getElementById|querySelector)"); analysis.Issues.Add(new ServiceIssue { Title = "Direct DOM manipulation in service", Description = "Services should not access the DOM. Use Renderer2.", Severity = Severity.High, Line = content[..m.Index].Count(c => c == '\n') + 1 }); }

            var consoleLogs = Regex.Matches(content, @"console\.(log|debug|info|warn)\s*\(").Count;
            if (consoleLogs > 0)
                analysis.Issues.Add(new ServiceIssue { Title = $"Console logging ({consoleLogs} call(s))", Description = "Use a logging service for production.", Severity = Severity.Low });

            var hardcoded = Regex.Matches(content, @"['""`]https?://[^'""`\n]+");
            if (hardcoded.Count > 0)
            {
                var ln = content[..hardcoded[0].Index].Count(c => c == '\n') + 1;
                var lc = ln <= lines.Length ? lines[ln - 1].Trim() : "";
                if (!lc.StartsWith("//"))
                    analysis.Issues.Add(new ServiceIssue { Title = $"Hardcoded URL(s) ({hardcoded.Count})", Description = "Use environment configuration for base URLs.", Severity = Severity.Medium, Line = ln, CodeSnippet = lc.Length > 120 ? lc[..120] + "..." : lc });
            }

            if (Regex.IsMatch(content, @"setInterval\s*\("))
            { var m = Regex.Match(content, @"setInterval\s*\("); analysis.Issues.Add(new ServiceIssue { Title = "setInterval in service", Description = "Use RxJS interval() with proper subscription management.", Severity = Severity.High, Line = content[..m.Index].Count(c => c == '\n') + 1 }); }

            if (Regex.IsMatch(content, @"\bwindow\.") && !Regex.IsMatch(content, @"@Inject\s*\(\s*(WINDOW|DOCUMENT)"))
                analysis.Issues.Add(new ServiceIssue { Title = "Direct window/document access", Description = "Use DOCUMENT injection token or create a window token.", Severity = Severity.Medium });

            if (Regex.IsMatch(content, @"(localStorage|sessionStorage)\.(get|set|remove)Item"))
            { var m = Regex.Match(content, @"(localStorage|sessionStorage)\.(get|set|remove)Item"); analysis.Issues.Add(new ServiceIssue { Title = "Direct browser storage access", Description = "Use a dedicated storage service.", Severity = Severity.Medium, Line = content[..m.Index].Count(c => c == '\n') + 1 }); }

            if (analysis.MethodCount > 15)
                analysis.Issues.Add(new ServiceIssue { Title = $"Large service ({analysis.MethodCount} methods)", Description = "Split into smaller, focused services (SRP).", Severity = Severity.Medium });

            var privateFields = Regex.Matches(content, @"private\s+\w+\s*[=:]").Count;
            if (privateFields > 3 && !Regex.IsMatch(content, @"(BehaviorSubject|ReplaySubject|Subject|signal|WritableSignal)"))
                analysis.Issues.Add(new ServiceIssue { Title = $"State without reactive patterns ({privateFields} fields)", Description = "Use BehaviorSubject/Signal for reactive updates.", Severity = Severity.Low });
        }
        catch (Exception ex)
        {
            analysis.Issues.Add(new ServiceIssue { Title = "Analysis error", Description = $"Could not analyze: {ex.Message}", Severity = Severity.Info });
        }

        return analysis;
    }
}
