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
        // Ranked first, written second. This used to be one UPDATE whose subquery
        // counted the peers ahead of each row - but SQLite writes the rows one at a
        // time and the subquery sees the ones it has already rewritten, so any list
        // whose order no longer followed its ids came out tied. Move C above B, delete
        // A, and B and C both landed on 1: the tie went back to id order and the move
        // was silently undone. The window function is evaluated over the rows as they
        // stood before any of them changed.
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            WITH ranked AS (
                SELECT {idColumn} AS RowId,
                       ROW_NUMBER() OVER (ORDER BY SortOrder, {idColumn}) - 1 AS NewOrder
                FROM {table}
                WHERE ResearchProjectId = @ProjectId)
            UPDATE {table} SET SortOrder = ranked.NewOrder
            FROM ranked
            WHERE {table}.{idColumn} = ranked.RowId
              AND {table}.SortOrder <> ranked.NewOrder;";
        cmd.Parameters.AddWithValue("@ProjectId", projectId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
