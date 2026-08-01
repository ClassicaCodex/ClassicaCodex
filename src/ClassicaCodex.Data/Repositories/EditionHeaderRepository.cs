using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

/// <summary>
/// The publication metadata an edition's TEI file states about itself, as
/// stored at ingest.
///
/// Kept in the database rather than re-read from the source file each time
/// it's shown, so the details view runs off the library like everything else
/// - see the EditionHeaders comment in SchemaInitializer for why that
/// mattered enough to add a table for.
/// </summary>
public class EditionHeaderRepository
{
    /// <summary>
    /// Replaces whatever was stored for this edition.
    ///
    /// Delete-then-insert rather than an update: re-ingesting a corpus after
    /// an upstream correction should leave the stored header matching the
    /// file exactly, including fields the new version has dropped. An UPDATE
    /// of only the fields present would leave stale values behind for the
    /// ones that aren't.
    /// </summary>
    public async Task SaveAsync(
        int editionId, EditionHeader header, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        await using (var deleteHeader = conn.CreateCommand())
        {
            deleteHeader.Transaction = (SqliteTransaction)transaction;
            deleteHeader.CommandText =
                "DELETE FROM EditionResponsibilities WHERE EditionId = @EditionId; " +
                "DELETE FROM EditionHeaders WHERE EditionId = @EditionId;";
            deleteHeader.Parameters.AddWithValue("@EditionId", editionId);
            await deleteHeader.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = conn.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = @"
                INSERT INTO EditionHeaders
                    (EditionId, Title, Author, Publisher, PublicationDate,
                     PublicationPlace, SourceDescription, EditionStatement, Availability)
                VALUES
                    (@EditionId, @Title, @Author, @Publisher, @PublicationDate,
                     @PublicationPlace, @SourceDescription, @EditionStatement, @Availability);";

            insert.Parameters.AddWithValue("@EditionId", editionId);
            insert.Parameters.AddWithValue("@Title", (object?)header.Title ?? DBNull.Value);
            insert.Parameters.AddWithValue("@Author", (object?)header.Author ?? DBNull.Value);
            insert.Parameters.AddWithValue("@Publisher", (object?)header.Publisher ?? DBNull.Value);
            insert.Parameters.AddWithValue("@PublicationDate", (object?)header.PublicationDate ?? DBNull.Value);
            insert.Parameters.AddWithValue("@PublicationPlace", (object?)header.PublicationPlace ?? DBNull.Value);
            insert.Parameters.AddWithValue("@SourceDescription", (object?)header.SourceDescription ?? DBNull.Value);
            insert.Parameters.AddWithValue("@EditionStatement", (object?)header.EditionStatement ?? DBNull.Value);
            insert.Parameters.AddWithValue("@Availability", (object?)header.Availability ?? DBNull.Value);

            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var i = 0; i < header.Responsibilities.Count; i++)
        {
            await using var insertResp = conn.CreateCommand();
            insertResp.Transaction = (SqliteTransaction)transaction;
            insertResp.CommandText =
                "INSERT INTO EditionResponsibilities (EditionId, SortOrder, Text) " +
                "VALUES (@EditionId, @SortOrder, @Text);";
            insertResp.Parameters.AddWithValue("@EditionId", editionId);
            insertResp.Parameters.AddWithValue("@SortOrder", i);
            insertResp.Parameters.AddWithValue("@Text", header.Responsibilities[i]);
            await insertResp.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Null when this edition has no stored header - either it was ingested
    /// before headers were recorded, or its file genuinely carried none.
    /// </summary>
    public async Task<EditionHeader?> GetAsync(int editionId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        EditionHeader? header = null;

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT Title, Author, Publisher, PublicationDate, PublicationPlace,
                       SourceDescription, EditionStatement, Availability
                FROM EditionHeaders
                WHERE EditionId = @EditionId;";
            cmd.Parameters.AddWithValue("@EditionId", editionId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                header = new EditionHeader
                {
                    EditionId = editionId,
                    Title = reader.IsDBNull(0) ? null : reader.GetString(0),
                    Author = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Publisher = reader.IsDBNull(2) ? null : reader.GetString(2),
                    PublicationDate = reader.IsDBNull(3) ? null : reader.GetString(3),
                    PublicationPlace = reader.IsDBNull(4) ? null : reader.GetString(4),
                    SourceDescription = reader.IsDBNull(5) ? null : reader.GetString(5),
                    EditionStatement = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Availability = reader.IsDBNull(7) ? null : reader.GetString(7)
                };
            }
        }

        if (header == null) return null;

        var responsibilities = new List<string>();

        await using (var respCmd = conn.CreateCommand())
        {
            respCmd.CommandText =
                "SELECT Text FROM EditionResponsibilities WHERE EditionId = @EditionId ORDER BY SortOrder;";
            respCmd.Parameters.AddWithValue("@EditionId", editionId);

            await using var reader = await respCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                responsibilities.Add(reader.GetString(0));
            }
        }

        header.Responsibilities = responsibilities;
        return header;
    }
}
