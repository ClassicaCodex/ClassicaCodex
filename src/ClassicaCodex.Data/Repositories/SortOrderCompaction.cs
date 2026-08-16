using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

/// <summary>
/// Closes the gaps a delete leaves in a project-scoped SortOrder column.
///
/// Every list in the Research Bench gives a new row a SortOrder of "however many
/// are currently showing". That is only correct while the numbering is dense: once
/// a delete leaves a gap, the next row added ties with a survivor and SQLite breaks
/// the tie by id, so a new item lands in the middle of the list instead of at the
/// end. It looks like the list reordered itself for no reason, and the researcher
/// has no way to put it back except by dragging every row.
/// </summary>
internal static class SortOrderCompaction
{
    /// <summary>
    /// Renumbers one project's rows densely from 0, preserving their current order and
    /// breaking ties by id - the same order the list itself uses.
    /// </summary>
    /// <param name="table">
    /// A table with a ResearchProjectId and a SortOrder. Interpolated into the SQL
    /// rather than parameterized, because identifiers cannot be parameterized; every
    /// caller passes a literal, and nothing here comes from the researcher.
    /// </param>
    /// <param name="idColumn">That table's primary key, used only to break ties.</param>
    public static async Task RenumberAsync(SqliteConnection conn, string table, string idColumn,
        long projectId, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            UPDATE {table} SET SortOrder = (
                SELECT COUNT(*) FROM {table} peer
                WHERE peer.ResearchProjectId = {table}.ResearchProjectId
                  AND (peer.SortOrder < {table}.SortOrder
                       OR (peer.SortOrder = {table}.SortOrder
                           AND peer.{idColumn} < {table}.{idColumn})))
            WHERE ResearchProjectId = @ProjectId;";
        cmd.Parameters.AddWithValue("@ProjectId", projectId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
