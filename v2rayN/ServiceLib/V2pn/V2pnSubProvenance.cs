namespace ServiceLib;

/// <summary>V2pn: marks what a subscription brought, so the mark outlives moving a server to another group.</summary>
public static class V2pnSubProvenance
{
    /// <summary>Marks every server the subscription <paramref name="subId"/> has right after an update.</summary>
    public static async Task MarkAsync(string? subId)
    {
        if (subId.IsNullOrEmpty())
        {
            return;
        }
        try
        {
            await SQLiteHelper.Instance.RunInTransactionAsync(db =>
                db.Execute("UPDATE ProfileItem SET V2pnFromSub = 1 WHERE Subid = ? AND IsSub = 1 AND V2pnFromSub = 0", subId));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnSubProvenance), ex);
        }
    }
}
