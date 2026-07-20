using System.Reflection;
using FPTUniRAG.Pages;
using Xunit;

namespace FPTUniRAG.Tests.Architecture;

public sealed class LayerDependencyTests
{
    private const string DataAccessNamespace = "FPTUniRAG.DataAccessLayer";

    [Fact]
    public void Presentation_types_do_not_depend_on_data_access_types()
    {
        var presentationAssembly = typeof(IndexModel).Assembly;
        var violations = presentationAssembly
            .GetTypes()
            .Where(IsPresentationType)
            .SelectMany(GetReferencedTypes)
            .Where(type => type.Namespace?.StartsWith(DataAccessNamespace, StringComparison.Ordinal) == true)
            .Select(type => type.FullName!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Presentation must call business services, not repositories or database types. Violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Presentation_source_does_not_bypass_services_except_in_program_di_registration()
    {
        var presentationDirectory = Path.Combine(FindSolutionRoot(), "FPTUniRAG");
        var violations = Directory
            .EnumerateFiles(presentationDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => !string.Equals(Path.GetFileName(path), "Program.cs", StringComparison.OrdinalIgnoreCase))
            .Select(path => new { Path = path, Source = File.ReadAllText(path) })
            .Where(file =>
                file.Source.Contains(DataAccessNamespace, StringComparison.Ordinal)
                || file.Source.Contains("AppDbContext", StringComparison.Ordinal)
                || file.Source.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(FindSolutionRoot(), file.Path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Only Program.cs may reference data-access types for DI registration. Violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Business_layer_uses_repositories_instead_of_database_contexts()
    {
        var solutionRoot = FindSolutionRoot();
        var businessDirectory = Path.Combine(solutionRoot, "FPTUniRAG.BusinessLayer");
        var violations = Directory
            .EnumerateFiles(businessDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => new { Path = path, Source = File.ReadAllText(path) })
            .Where(file =>
                file.Source.Contains("FPTUniRAG.DataAccessLayer.Context", StringComparison.Ordinal)
                || file.Source.Contains("AppDbContext", StringComparison.Ordinal)
                || file.Source.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(solutionRoot, file.Path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Business services must access persistence through repositories. Violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static bool IsPresentationType(Type type) =>
        type.Namespace?.StartsWith("FPTUniRAG.Pages", StringComparison.Ordinal) == true
        || type.Namespace?.StartsWith("FPTUniRAG.Endpoints", StringComparison.Ordinal) == true
        || type.Namespace?.StartsWith("FPTUniRAG.Hubs", StringComparison.Ordinal) == true;

    private static IEnumerable<Type> GetReferencedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        foreach (var field in type.GetFields(flags))
            yield return Unwrap(field.FieldType);

        foreach (var property in type.GetProperties(flags))
            yield return Unwrap(property.PropertyType);

        foreach (var constructor in type.GetConstructors(flags))
        foreach (var parameter in constructor.GetParameters())
            yield return Unwrap(parameter.ParameterType);

        foreach (var method in type.GetMethods(flags))
        {
            yield return Unwrap(method.ReturnType);
            foreach (var parameter in method.GetParameters())
                yield return Unwrap(parameter.ParameterType);
        }
    }

    private static Type Unwrap(Type type)
    {
        while (type.HasElementType && type.GetElementType() is { } elementType)
            type = elementType;

        if (type.IsGenericType)
            return type.GetGenericArguments().Select(Unwrap).FirstOrDefault(IsDataAccessType) ?? type;

        return type;
    }

    private static bool IsDataAccessType(Type type) =>
        type.Namespace?.StartsWith(DataAccessNamespace, StringComparison.Ordinal) == true;

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FPTUniRAG.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the solution root containing FPTUniRAG.slnx.");
    }

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar).Any(segment =>
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
}
