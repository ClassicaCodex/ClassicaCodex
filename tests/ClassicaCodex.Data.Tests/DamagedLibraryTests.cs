using ClassicaCodex.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// A library file that is not a database - damaged, or something else saved
/// under its name - fails to open, and must not stay open after failing.
/// </summary>
[Collection("Database")]
public class DamagedLibraryTests
{
    [Fact]
    public async Task A_file_that_is_not_a_database_is_let_go_when_it_fails_to_open()
    {
        var directory = Path.Combine(Path.GetTempPath(), "classicacodex-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "classicacodex.db");
        await File.WriteAllTextAsync(file, "This is not a database, and SQLite will say so at the first statement.");

        try
        {
            DbConnectionFactory.Configure(file, remember: false);
            await Assert.ThrowsAsync<SqliteException>(() => SchemaInitializer.EnsureSchemaAsync());

            // Nothing collected, nothing cleared by the test: the failed
            // connection has to have let go of the file itself. Until it did,
            // this delete failed with "being used by another process".
            File.Delete(file);
            Assert.False(File.Exists(file));
        }
        finally
        {
            DbConnectionFactory.Forget();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
