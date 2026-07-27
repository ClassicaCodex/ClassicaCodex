using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public class ArtifactRepository
{
    /// <summary>
    /// Wipes and reloads both tables in one transaction - see the schema
    /// comment on Artifacts for why a clean replace, not an incremental
    /// upsert, is the right model for a downloaded reference dataset.
    /// </summary>
    public async Task ReplaceAllAsync(
        List<Artifact> artifacts, List<ArtifactImage> images, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken);

        await using (var clearImagesCmd = conn.CreateCommand())
        {
            clearImagesCmd.Transaction = transaction;
            clearImagesCmd.CommandText = "DELETE FROM ArtifactImages;";
            await clearImagesCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var clearArtifactsCmd = conn.CreateCommand())
        {
            clearArtifactsCmd.Transaction = transaction;
            clearArtifactsCmd.CommandText = "DELETE FROM Artifacts;";
            await clearArtifactsCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insertArtifactCmd = conn.CreateCommand();
        insertArtifactCmd.Transaction = transaction;
        insertArtifactCmd.CommandText = @"
            INSERT INTO Artifacts
                (ArtifactId, Type, Name, Region, Context, MatchedPlaceName, Period,
                 StartDate, EndDate, Collection, Material, Location, Description, PrimaryCitation)
            VALUES
                (@ArtifactId, @Type, @Name, @Region, @Context, @MatchedPlaceName, @Period,
                 @StartDate, @EndDate, @Collection, @Material, @Location, @Description, @PrimaryCitation);";

        var pArtifactId = insertArtifactCmd.Parameters.Add("@ArtifactId", SqliteType.Text);
        var pType = insertArtifactCmd.Parameters.Add("@Type", SqliteType.Text);
        var pName = insertArtifactCmd.Parameters.Add("@Name", SqliteType.Text);
        var pRegion = insertArtifactCmd.Parameters.Add("@Region", SqliteType.Text);
        var pContext = insertArtifactCmd.Parameters.Add("@Context", SqliteType.Text);
        var pMatchedPlaceName = insertArtifactCmd.Parameters.Add("@MatchedPlaceName", SqliteType.Text);
        var pPeriod = insertArtifactCmd.Parameters.Add("@Period", SqliteType.Text);
        var pStartDate = insertArtifactCmd.Parameters.Add("@StartDate", SqliteType.Text);
        var pEndDate = insertArtifactCmd.Parameters.Add("@EndDate", SqliteType.Text);
        var pCollection = insertArtifactCmd.Parameters.Add("@Collection", SqliteType.Text);
        var pMaterial = insertArtifactCmd.Parameters.Add("@Material", SqliteType.Text);
        var pLocation = insertArtifactCmd.Parameters.Add("@Location", SqliteType.Text);
        var pDescription = insertArtifactCmd.Parameters.Add("@Description", SqliteType.Text);
        var pPrimaryCitation = insertArtifactCmd.Parameters.Add("@PrimaryCitation", SqliteType.Text);

        foreach (var a in artifacts)
        {
            pArtifactId.Value = a.ArtifactId;
            pType.Value = a.Type;
            pName.Value = (object?)a.Name ?? DBNull.Value;
            pRegion.Value = (object?)a.Region ?? DBNull.Value;
            pContext.Value = (object?)a.Context ?? DBNull.Value;
            pMatchedPlaceName.Value = (object?)a.MatchedPlaceName ?? DBNull.Value;
            pPeriod.Value = (object?)a.Period ?? DBNull.Value;
            pStartDate.Value = (object?)a.StartDate ?? DBNull.Value;
            pEndDate.Value = (object?)a.EndDate ?? DBNull.Value;
            pCollection.Value = (object?)a.Collection ?? DBNull.Value;
            pMaterial.Value = (object?)a.Material ?? DBNull.Value;
            pLocation.Value = (object?)a.Location ?? DBNull.Value;
            pDescription.Value = (object?)a.Description ?? DBNull.Value;
            pPrimaryCitation.Value = (object?)a.PrimaryCitation ?? DBNull.Value;
            await insertArtifactCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insertImageCmd = conn.CreateCommand();
        insertImageCmd.Transaction = transaction;
        insertImageCmd.CommandText = @"
            INSERT INTO ArtifactImages (ArtifactId, ImageId, Caption, Credits)
            VALUES (@ArtifactId, @ImageId, @Caption, @Credits);";

        var ipArtifactId = insertImageCmd.Parameters.Add("@ArtifactId", SqliteType.Text);
        var ipImageId = insertImageCmd.Parameters.Add("@ImageId", SqliteType.Text);
        var ipCaption = insertImageCmd.Parameters.Add("@Caption", SqliteType.Text);
        var ipCredits = insertImageCmd.Parameters.Add("@Credits", SqliteType.Text);

        foreach (var img in images)
        {
            ipArtifactId.Value = img.ArtifactId;
            ipImageId.Value = img.ImageId;
            ipCaption.Value = (object?)img.Caption ?? DBNull.Value;
            ipCredits.Value = (object?)img.Credits ?? DBNull.Value;
            await insertImageCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> HasDataAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM Artifacts);";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    /// <summary>Every artifact whose findspot resolved to this place at ingest time - what the Places Map's artifact browser lists.</summary>
    public async Task<List<Artifact>> GetByPlaceNameAsync(string placeName, CancellationToken cancellationToken = default)
    {
        var results = new List<Artifact>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ArtifactId, Type, Name, Region, Context, MatchedPlaceName, Period,
                   StartDate, EndDate, Collection, Material, Location, Description, PrimaryCitation
            FROM Artifacts
            WHERE MatchedPlaceName = @PlaceName
            ORDER BY Type, Name;";
        cmd.Parameters.AddWithValue("@PlaceName", placeName);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadArtifact(reader));
        }

        return results;
    }

    /// <summary>
    /// A broader, fuzzier match than GetByPlaceNameAsync - there's no
    /// precise pre-resolved field for "what myth or figure is this artifact
    /// about" the way MatchedPlaceName is for findspots, so this is a plain
    /// text search across Name, Description, and Context instead. Good
    /// enough to find "Herakles" in a sculpture's subject description or a
    /// vase's decoration text, at the cost of being noisier than an exact
    /// match - it can't tell a genuine depiction from an incidental mention.
    /// </summary>
    public async Task<List<Artifact>> SearchByTextAsync(string term, CancellationToken cancellationToken = default)
    {
        var results = new List<Artifact>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ArtifactId, Type, Name, Region, Context, MatchedPlaceName, Period,
                   StartDate, EndDate, Collection, Material, Location, Description, PrimaryCitation
            FROM Artifacts
            WHERE Name LIKE @Term OR Description LIKE @Term OR Context LIKE @Term
            ORDER BY Type, Name;";
        cmd.Parameters.AddWithValue("@Term", $"%{term}%");

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadArtifact(reader));
        }

        return results;
    }

    /// <summary>Shared by every query above - one place to update if a column is ever added, rather than a second copy quietly drifting out of sync.</summary>
    private static Artifact ReadArtifact(SqliteDataReader reader) => new()
    {
        ArtifactId = reader.GetString(0),
        Type = reader.GetString(1),
        Name = reader.IsDBNull(2) ? null : reader.GetString(2),
        Region = reader.IsDBNull(3) ? null : reader.GetString(3),
        Context = reader.IsDBNull(4) ? null : reader.GetString(4),
        MatchedPlaceName = reader.IsDBNull(5) ? null : reader.GetString(5),
        Period = reader.IsDBNull(6) ? null : reader.GetString(6),
        StartDate = reader.IsDBNull(7) ? null : reader.GetString(7),
        EndDate = reader.IsDBNull(8) ? null : reader.GetString(8),
        Collection = reader.IsDBNull(9) ? null : reader.GetString(9),
        Material = reader.IsDBNull(10) ? null : reader.GetString(10),
        Location = reader.IsDBNull(11) ? null : reader.GetString(11),
        Description = reader.IsDBNull(12) ? null : reader.GetString(12),
        PrimaryCitation = reader.IsDBNull(13) ? null : reader.GetString(13)
    };

    /// <summary>Every photo linked to one artifact - what the image browser flips through.</summary>
    public async Task<List<ArtifactImage>> GetImagesForArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        var results = new List<ArtifactImage>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ArtifactId, ImageId, Caption, Credits FROM ArtifactImages WHERE ArtifactId = @ArtifactId;";
        cmd.Parameters.AddWithValue("@ArtifactId", artifactId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ArtifactImage
            {
                ArtifactId = reader.GetString(0),
                ImageId = reader.GetString(1),
                Caption = reader.IsDBNull(2) ? null : reader.GetString(2),
                Credits = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }

        return results;
    }
}
