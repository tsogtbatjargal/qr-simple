using System.ComponentModel;
using System.Net;
using System.Xml.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace QrSimple.Mcp;

[McpServerToolType]
public static partial class WorkspaceTools
{
    private static readonly HttpClient ApiClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".vscode",
        ".codex",
        ".idea",
        "bin",
        "obj",
        "node_modules",
        "TestResults",
        ".terraform",
    };

    private static readonly HashSet<string> SearchableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".csproj",
        ".json",
        ".md",
        ".props",
        ".targets",
        ".toml",
        ".txt",
        ".yml",
        ".yaml",
    };

    [McpServerTool, Description("Searches text files in the qr-simple workspace for a string.")]
    public static WorkspaceSearchResult WorkspaceSearch(
        [Description("Text to search for.")] string query,
        [Description("Optional relative path prefix to narrow the search.")] string? relativePathPrefix = null,
        [Description("Maximum number of matches to return.")] int maxResults = 25)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new WorkspaceSearchResult(GetWorkspaceRoot(), query, Array.Empty<SearchMatch>(), false, "Query cannot be empty.");
        }

        var root = GetWorkspaceRoot();
        var prefix = NormalizeRelativePrefix(relativePathPrefix);
        var matches = new List<SearchMatch>();

        foreach (var file in EnumerateSearchableFiles(root, prefix))
        {
            var relativePath = Path.GetRelativePath(root, file);
            var lineNumber = 0;

            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (!line.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matches.Add(new SearchMatch(relativePath, lineNumber, line.TrimEnd()));
                if (matches.Count >= maxResults)
                {
                    return new WorkspaceSearchResult(root, query, matches, true, null);
                }
            }
        }

        return new WorkspaceSearchResult(root, query, matches, false, null);
    }

    [McpServerTool, Description("Reads a text file from the qr-simple workspace.")]
    public static WorkspaceReadResult WorkspaceRead(
        [Description("Path relative to the workspace root.")] string relativePath,
        [Description("First line to include, starting at 1.")] int startLine = 1,
        [Description("Maximum number of lines to return.")] int maxLines = 200)
    {
        var root = GetWorkspaceRoot();
        var fullPath = ResolveWorkspacePath(root, relativePath);

        if (!File.Exists(fullPath))
        {
            return WorkspaceReadResult.NotFound(root, relativePath);
        }

        startLine = Math.Max(startLine, 1);
        maxLines = Math.Max(maxLines, 1);

        var lines = new List<WorkspaceLine>();
        var currentLine = 0;
        var endLine = startLine + maxLines - 1;

        foreach (var line in File.ReadLines(fullPath))
        {
            currentLine++;
            if (currentLine < startLine)
            {
                continue;
            }

            if (currentLine > endLine)
            {
                break;
            }

            lines.Add(new WorkspaceLine(currentLine, line));
        }

        var lastReturnedLine = lines.Count == 0 ? startLine - 1 : lines[^1].LineNumber;
        return new WorkspaceReadResult(root, Path.GetRelativePath(root, fullPath), startLine, lastReturnedLine, lines, currentLine > endLine);
    }

    [McpServerTool, Description("Checks whether the qr-simple API is reachable and returns the current /categories response.")]
    public static async Task<AppHealthResult> AppHealth()
    {
        var baseUrl = GetApiBaseUrl();
        var requestUri = new Uri(new Uri(baseUrl, UriKind.Absolute), "/categories");

        try
        {
            using var response = await ApiClient.GetAsync(requestUri).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            return new AppHealthResult(
                baseUrl,
                true,
                (int)response.StatusCode,
                response.ReasonPhrase,
                body,
                body.Trim() == "[]");
        }
        catch (Exception ex)
        {
            return new AppHealthResult(baseUrl, false, null, ex.GetType().Name, null, false);
        }
    }

    [McpServerTool, Description("Summarizes the API routes declared in src/QrSimple.Api/Program.cs.")]
    public static RouteInventoryResult RouteInventory()
    {
        var root = GetWorkspaceRoot();
        var programPath = Path.Combine(root, "src", "QrSimple.Api", "Program.cs");

        if (!File.Exists(programPath))
        {
            return new RouteInventoryResult(Path.GetRelativePath(root, programPath), Array.Empty<RouteDefinition>(), "Program.cs was not found.");
        }

        var programText = File.ReadAllText(programPath);
        var matches = RouteRegex().Matches(programText);
        var routes = new List<RouteDefinition>();

        foreach (Match match in matches)
        {
            var verb = match.Groups["verb"].Value.ToUpperInvariant();
            var path = match.Groups["path"].Value;
            var rest = match.Groups["rest"].Value;
            var requiresAuth = rest.Contains("RequireAuthorization()", StringComparison.Ordinal);
            var roles = ExtractRoles(rest).ToArray();

            routes.Add(new RouteDefinition(path, verb, requiresAuth, roles));
        }

        return new RouteInventoryResult(Path.GetRelativePath(root, programPath), routes, null);
    }

    [McpServerTool, Description("Returns a compact summary of the authenticated API routes and their roles.")]
    public static RouteAuthSummaryResult RouteAuthSummary()
    {
        var inventory = RouteInventory();
        if (inventory.Error is not null)
        {
            return new RouteAuthSummaryResult(inventory.ProgramPath, Array.Empty<RouteAuthEntry>(), inventory.Error);
        }

        var routes = inventory.Routes
            .Where(route => route.RequiresAuthorization || route.Roles.Count > 0)
            .Select(route => new RouteAuthEntry(route.Verb, route.Path, route.RequiresAuthorization, route.Roles))
            .ToArray();

        return new RouteAuthSummaryResult(inventory.ProgramPath, routes, null);
    }

    [McpServerTool, Description("Reads the newest TRX test result under the workspace and summarizes failures.")]
    public static TestFailureSummaryResult LatestTestFailures()
    {
        var root = GetWorkspaceRoot();
        var trxFiles = Directory.EnumerateFiles(root, "*.trx", SearchOption.AllDirectories)
            .Where(path => !IsIgnoredPath(path))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();

        if (trxFiles.Length == 0)
        {
            return new TestFailureSummaryResult(null, Array.Empty<TestFailure>(), "No .trx files were found in the workspace.");
        }

        var latest = trxFiles[0];

        try
        {
            var doc = XDocument.Load(latest.FullName);
            var failures = doc
                .Descendants()
                .Where(element => element.Name.LocalName == "UnitTestResult" &&
                                  string.Equals(element.Attribute("outcome")?.Value, "Failed", StringComparison.OrdinalIgnoreCase))
                .Select(result =>
                {
                    var testName = result.Attribute("testName")?.Value ?? "(unknown test)";
                    var output = result.Descendants().FirstOrDefault(element => element.Name.LocalName == "Output");
                    var errorInfo = output?.Descendants().FirstOrDefault(element => element.Name.LocalName == "ErrorInfo");
                    var message = errorInfo?.Descendants().FirstOrDefault(element => element.Name.LocalName == "Message")?.Value?.Trim();
                    var stackTrace = errorInfo?.Descendants().FirstOrDefault(element => element.Name.LocalName == "StackTrace")?.Value?.Trim();

                    return new TestFailure(
                        testName,
                        message,
                        stackTrace,
                        result.Attribute("computerName")?.Value,
                        result.Attribute("duration")?.Value);
                })
                .ToArray();

            return new TestFailureSummaryResult(Path.GetRelativePath(root, latest.FullName), failures, null);
        }
        catch (Exception ex)
        {
            return new TestFailureSummaryResult(Path.GetRelativePath(root, latest.FullName), Array.Empty<TestFailure>(), $"Failed to parse TRX: {ex.Message}");
        }
    }

    private static string GetWorkspaceRoot()
    {
        var envRoot = Environment.GetEnvironmentVariable("QR_SIMPLE_WORKSPACE_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot))
        {
            return Path.GetFullPath(envRoot);
        }

        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "tests")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string GetApiBaseUrl()
    {
        var configured = Environment.GetEnvironmentVariable("QR_SIMPLE_API_BASE_URL");
        return string.IsNullOrWhiteSpace(configured) ? "http://127.0.0.1:5078" : configured;
    }

    private static IEnumerable<string> EnumerateSearchableFiles(string root, string? relativePathPrefix)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (!IsSearchableFile(file))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(root, file);
            if (!string.IsNullOrWhiteSpace(relativePathPrefix) &&
                !relativePath.StartsWith(relativePathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return file;
        }
    }

    private static bool IsSearchableFile(string filePath)
    {
        if (IsIgnoredPath(filePath))
        {
            return false;
        }

        var directory = new DirectoryInfo(Path.GetDirectoryName(filePath) ?? ".");
        while (directory is not null)
        {
            if (IgnoredDirectoryNames.Contains(directory.Name))
            {
                return false;
            }

            directory = directory.Parent;
        }

        return SearchableExtensions.Contains(Path.GetExtension(filePath)) || Path.GetFileName(filePath) == "AGENTS.md";
    }

    private static bool IsIgnoredPath(string filePath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(filePath) ?? ".");
        while (directory is not null)
        {
            if (IgnoredDirectoryNames.Contains(directory.Name))
            {
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }

    private static string? NormalizeRelativePrefix(string? relativePathPrefix)
    {
        if (string.IsNullOrWhiteSpace(relativePathPrefix))
        {
            return null;
        }

        return relativePathPrefix.Trim().TrimStart('.', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string ResolveWorkspacePath(string root, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal) && !string.Equals(fullPath, root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Path must stay inside the workspace root.");
        }

        return fullPath;
    }

    private static IEnumerable<string> ExtractRoles(string routeBlock)
    {
        var roleMatches = RoleRegex().Matches(routeBlock);
        var roles = new List<string>();

        foreach (Match roleMatch in roleMatches)
        {
            var args = roleMatch.Groups["args"].Value;
            foreach (Match stringMatch in QuotedStringRegex().Matches(args))
            {
                roles.Add(WebUtility.HtmlDecode(stringMatch.Groups["value"].Value));
            }
        }

        return roles;
    }

    [GeneratedRegex(@"app\.Map(?<verb>\w+)\(\s*""(?<path>[^""]+)""(?<rest>.*?);\s*", RegexOptions.Singleline)]
    private static partial Regex RouteRegex();

    [GeneratedRegex(@"RequireRoleFilter\((?<args>[^)]*)\)", RegexOptions.Singleline)]
    private static partial Regex RoleRegex();

    [GeneratedRegex(@"""(?<value>[^""]+)""", RegexOptions.Singleline)]
    private static partial Regex QuotedStringRegex();
}

public sealed record WorkspaceSearchResult(
    string WorkspaceRoot,
    string Query,
    IReadOnlyList<SearchMatch> Matches,
    bool Truncated,
    string? Error);

public sealed record SearchMatch(string Path, int LineNumber, string Excerpt);

public sealed record WorkspaceReadResult(
    string WorkspaceRoot,
    string RelativePath,
    int StartLine,
    int LastLineRead,
    IReadOnlyList<WorkspaceLine> Lines,
    bool Truncated,
    string? Error = null)
{
    public static WorkspaceReadResult NotFound(string root, string relativePath)
        => new(root, relativePath, 1, 1, Array.Empty<WorkspaceLine>(), false, "File not found.");
}

public sealed record WorkspaceLine(int LineNumber, string Text);

public sealed record AppHealthResult(
    string BaseUrl,
    bool Reachable,
    int? StatusCode,
    string? StatusText,
    string? Body,
    bool EmptyCategoriesResponse);

public sealed record RouteInventoryResult(
    string ProgramPath,
    IReadOnlyList<RouteDefinition> Routes,
    string? Error);

public sealed record RouteAuthSummaryResult(
    string ProgramPath,
    IReadOnlyList<RouteAuthEntry> Routes,
    string? Error);

public sealed record RouteAuthEntry(
    string Verb,
    string Path,
    bool RequiresAuthorization,
    IReadOnlyList<string> Roles);

public sealed record TestFailureSummaryResult(
    string? TrxPath,
    IReadOnlyList<TestFailure> Failures,
    string? Error);

public sealed record TestFailure(
    string TestName,
    string? Message,
    string? StackTrace,
    string? ComputerName,
    string? Duration);

public sealed record RouteDefinition(
    string Path,
    string Verb,
    bool RequiresAuthorization,
    IReadOnlyList<string> Roles);
