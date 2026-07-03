namespace FPTUniRAG.BusinessLayer.Accounts;

public sealed record ImportStudentsResult(IReadOnlyList<ImportStudentsRowResult> Rows)
{
    public int CreatedCount => Rows.Count(row => row.IsCreated);

    public int SkippedCount => Rows.Count - CreatedCount;
}
