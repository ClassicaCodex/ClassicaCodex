using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Removing an edition that has a stored header.
///
/// The header and its responsibility lines reference the edition and do not
/// cascade, and DeleteEditionAsync used to leave them, so the delete failed on
/// a foreign key for any edition with a header - which is any Perseus, Menota
/// or Oracc text. It surfaced when the Oracc importer stopped giving Maqlû a
/// sign pane and the old one had to come out; Menota's stale-edition cleanup
/// would have hit it the same way.
/// </summary>
[Collection("Database")]
public class EditionDeletionTests
{
    [Fact]
    public async Task AnEditionWithAHeaderCanBeDeleted()
    {
        using var db = await TempDatabase.CreateAsync();

        var authorId = await new AuthorRepository().UpsertAsync(new Author
        {
            CtsUrn = "oracc:cmawro/maqlu:cmawro", Name = "CMAwRo", Namespace = "oracc"
        });
        var workId = await new WorkRepository().UpsertAsync(new Work
        {
            AuthorId = authorId, CtsUrn = "oracc:cmawro/maqlu:Q002705", Title = "Maqlû 1"
        });
        var editionId = await new EditionRepository().UpsertAsync(new Edition
        {
            WorkId = workId, CtsUrn = "oracc:cmawro/maqlu:Q002705:cuneiform", Kind = EditionKind.Original, Language = "akk"
        });

        await new EditionHeaderRepository().SaveAsync(editionId, new EditionHeader
        {
            EditionId = editionId,
            Title = "Maqlû 1 (Cuneiform)",
            Responsibilities = new[] { "Tzvi Abusch (editor)" }
        });

        await new EditionRepository().DeleteEditionAsync(editionId);

        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT COUNT(*) FROM Editions WHERE EditionId = {editionId};"));
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT COUNT(*) FROM EditionHeaders WHERE EditionId = {editionId};"));
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT COUNT(*) FROM EditionResponsibilities WHERE EditionId = {editionId};"));
    }
}
