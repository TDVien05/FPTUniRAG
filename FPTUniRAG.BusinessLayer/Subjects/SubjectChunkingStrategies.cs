namespace FPTUniRAG.BusinessLayer.Subjects;

public static class     SubjectChunkingStrategies
{
    public const string Fixed = "fixed";
    public const string Semantic = "semantic";

    public static readonly IReadOnlyList<string> All = [Fixed, Semantic];

    public static bool IsSupported(string? value)
    {
        return All.Contains(Normalize(value), StringComparer.OrdinalIgnoreCase);
    }

    public static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Fixed
            : value.Trim().ToLowerInvariant();
    }

    public static string ToDisplayLabel(string? value)
    {
        return Normalize(value) switch
        {
            Semantic => "Semantic chunking",
            _ => "Fixed size"
        };
    }
}
