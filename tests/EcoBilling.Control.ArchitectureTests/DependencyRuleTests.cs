using System.Reflection;

namespace EcoBilling.Control.ArchitectureTests;

/// <summary>
/// Enforces the dependency directions of the solution.
///
/// Every rule is checked twice, because the two checks catch different mistakes:
/// the csproj check catches a reference that was declared but not yet used, which
/// compiled metadata would not show; the assembly check catches real coupling,
/// including coupling pulled in transitively through a NuGet package.
/// </summary>
public sealed class DependencyRuleTests
{
    private const string Domain = "EcoBilling.Control.Domain";
    private const string Application = "EcoBilling.Control.Application";
    private const string Infrastructure = "EcoBilling.Control.Infrastructure";
    private const string Api = "EcoBilling.Control.Api";
    private const string Provisioning = "EcoBilling.Control.Provisioning";

    private static readonly string[] SolutionProjects = [Domain, Application, Infrastructure, Api];

    private static readonly string[] ForbiddenInDomain =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions",
        "Npgsql",
        "Dapper",
        "Serilog",
    ];

    private static readonly string[] ForbiddenInApplication =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "Dapper",
    ];

    // ---------- Domain ----------

    [Fact]
    public void Domain_DeclaresNoProjectReferences()
    {
        Assert.Empty(SolutionLayout.ProjectReferencesOf(Domain));
    }

    [Fact]
    public void Domain_DeclaresNoPackageReferences()
    {
        Assert.Empty(SolutionLayout.PackageReferencesOf(Domain));
    }

    [Fact]
    public void Domain_CompilesAgainstTheBaseClassLibraryOnly()
    {
        var offenders = ReferencedAssemblyNames(Domain)
            .Where(name => !IsFrameworkAssembly(name))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{Domain} must depend on nothing but the base class library, but references: {string.Join(", ", offenders)}");
    }

    [Theory]
    [InlineData(Application)]
    [InlineData(Infrastructure)]
    [InlineData(Api)]
    public void Domain_DoesNotDependOnAnyOtherSolutionProject(string other)
    {
        Assert.DoesNotContain(other, ReferencedAssemblyNames(Domain), StringComparer.Ordinal);
    }

    [Fact]
    public void Domain_DoesNotDependOnInfrastructureTechnology()
    {
        AssertNoReferenceMatching(Domain, ForbiddenInDomain);
    }

    // ---------- Application ----------

    [Fact]
    public void Application_ReferencesDomainOnly()
    {
        Assert.Equal([Domain], SolutionLayout.ProjectReferencesOf(Application));
    }

    [Theory]
    [InlineData(Infrastructure)]
    [InlineData(Api)]
    public void Application_DoesNotDependOnInfrastructureOrApi(string forbidden)
    {
        Assert.DoesNotContain(forbidden, SolutionLayout.ProjectReferencesOf(Application), StringComparer.Ordinal);
        Assert.DoesNotContain(forbidden, ReferencedAssemblyNames(Application), StringComparer.Ordinal);
    }

    [Fact]
    public void Application_DoesNotDependOnAConcretePersistenceTechnology()
    {
        AssertNoReferenceMatching(Application, ForbiddenInApplication);
    }

    // ---------- Infrastructure ----------

    [Fact]
    public void Infrastructure_ReferencesApplicationAndDomainOnly()
    {
        Assert.Equal([Application, Domain], SolutionLayout.ProjectReferencesOf(Infrastructure));
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnApi()
    {
        Assert.DoesNotContain(Api, SolutionLayout.ProjectReferencesOf(Infrastructure), StringComparer.Ordinal);
        Assert.DoesNotContain(Api, ReferencedAssemblyNames(Infrastructure), StringComparer.Ordinal);
    }

    // ---------- Api ----------

    [Fact]
    public void Api_IsTheCompositionRoot()
    {
        var references = SolutionLayout.ProjectReferencesOf(Api);

        Assert.Contains(Application, references, StringComparer.Ordinal);
        Assert.Contains(Infrastructure, references, StringComparer.Ordinal);
    }

    [Fact]
    public void Api_IsReferencedByNoProductionProject()
    {
        foreach (var project in new[] { Domain, Application, Infrastructure })
        {
            Assert.DoesNotContain(Api, SolutionLayout.ProjectReferencesOf(project), StringComparer.Ordinal);
        }
    }

    // ---------- Provisioning ----------

    [Fact]
    public void Provisioning_IsReferencedByNoProductionProject()
    {
        // The provisioning console tool (Stage 6, Q4) is a leaf, consumer-only project,
        // exactly like the test projects -- nothing in Domain/Application/Infrastructure/Api
        // should ever depend on it.
        foreach (var project in new[] { Domain, Application, Infrastructure, Api })
        {
            Assert.DoesNotContain(Provisioning, SolutionLayout.ProjectReferencesOf(project), StringComparer.Ordinal);
        }
    }

    // ---------- Whole graph ----------

    [Fact]
    public void TheDependencyGraphHasNoCycles()
    {
        var graph = SolutionProjects.ToDictionary(
            p => p,
            SolutionLayout.ProjectReferencesOf,
            StringComparer.Ordinal);

        var settled = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in SolutionProjects)
        {
            Assert.False(
                HasCycle(project, graph, settled, visiting),
                $"A dependency cycle reaches {project}.");
        }
    }

    private static bool HasCycle(
        string project,
        IReadOnlyDictionary<string, IReadOnlyList<string>> graph,
        HashSet<string> settled,
        HashSet<string> visiting)
    {
        if (settled.Contains(project))
        {
            return false;
        }

        if (!visiting.Add(project))
        {
            return true;
        }

        if (graph.TryGetValue(project, out var dependencies))
        {
            foreach (var dependency in dependencies)
            {
                if (HasCycle(dependency, graph, settled, visiting))
                {
                    return true;
                }
            }
        }

        visiting.Remove(project);
        settled.Add(project);

        return false;
    }

    // ---------- helpers ----------

    private static void AssertNoReferenceMatching(string project, IEnumerable<string> forbiddenPrefixes)
    {
        var references = ReferencedAssemblyNames(project);

        foreach (var prefix in forbiddenPrefixes)
        {
            var offender = references.FirstOrDefault(r => r.StartsWith(prefix, StringComparison.Ordinal));

            Assert.True(offender is null, $"{project} must not reference {offender}.");
        }
    }

    private static IReadOnlyList<string> ReferencedAssemblyNames(string project) =>
        Assembly.Load(project)
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

    private static bool IsFrameworkAssembly(string name) =>
        name.StartsWith("System.", StringComparison.Ordinal) ||
        name is "System" or "netstandard" or "mscorlib";
}
