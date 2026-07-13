namespace FPTUniRAG.DataAccessLayer.Entities;

public sealed class StudentFreeQuotaSetting
{
    public short SettingId { get; set; } = 1;

    public long MonthlyTokenLimit { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
