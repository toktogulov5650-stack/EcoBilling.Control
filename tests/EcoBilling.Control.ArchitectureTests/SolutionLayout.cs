using System.Xml.Linq;

namespace EcoBilling.Control.ArchitectureTests;

/// <summary>
/// Locates the solution on disk so the architecture rules can be checked against the
/// project files themselves, not only against compiled metadata.
/// </summary>
internal static class SolutionLayout
{
    private const string SolutionFileName = "EcoBilling.Control.slnx";

    // Character codes keep this readable: a literal backslash escape next to a
    // forward slash in a collection expression is easy to misread and easy to break.
    private const char ForwardSlash = (char)47;
    private const char Backslash = (char)92;

    private static readonly char[] PathSeparators = [ForwardSlash, Backslash];

    public static DirectoryInfo Root { get; } = FindRoot();

    public static string ProjectFile(string projectName)
    {
        var path = Path.Combine(Root.FullName, "src", projectName, projectName + ".csproj");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Project file not found: {path}");
        }

        return path;
    }

    /// <summary>The project names referenced by <paramref name="projectName"/> in its csproj.</summary>
    public static IReadOnlyList<string> ProjectReferencesOf(string projectName)
    {
        var document = XDocument.Load(ProjectFile(projectName));

        return document
            .Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Path.GetFileNameWithoutExtension(LastSegment(v!)))
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The NuGet package ids referenced by <paramref name="projectName"/>.</summary>
    public static IReadOnlyList<string> PackageReferencesOf(string projectName)
    {
        var document = XDocument.Load(ProjectFile(projectName));

        return document
            .Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Returns the final segment of a project-relative path. Project files always use
    /// backslashes regardless of platform, so splitting on both separators is required
    /// for these rules to hold when CI runs on Linux.
    /// </summary>
    private static string LastSegment(string path)
    {
        var segments = path.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);

        return segments.Length == 0 ? path : segments[^1];
    }

    private static DirectoryInfo FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate {SolutionFileName} above {AppContext.BaseDirectory}.");
    }
}
