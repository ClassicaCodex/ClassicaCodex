using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

/// <summary>
/// The WHERE fragment restricting a search to a chosen set of works.
///
/// Shared rather than written per repository because more than one search
/// takes a scope now - keyword search, lemma-aware search, and morphology -
/// and they have to agree on it. They alias the Works table as `w`, so the
/// fragment does too; a query that aliases it differently cannot use this.
///
/// Parameters rather than an interpolated id list: work ids come from a
/// picker and are ints either way, but a query built by concatenation is one
/// refactor away from being built from something that isn't.
/// </summary>
internal static class WorkScope
{
    /// <summary>
    /// Returns the fragment and registers its parameters on the command.
    /// Empty when unrestricted, which is what an empty selection means - the
    /// picker returns nothing at all for "everything" rather than a list of
    /// every id.
    /// </summary>
    public static string Clause(SqliteCommand cmd, IReadOnlyList<int>? workIds, string keyword)
    {
        if (workIds == null || workIds.Count == 0) return string.Empty;

        var names = new List<string>();
        for (var i = 0; i < workIds.Count; i++)
        {
            names.Add($"@k{i}");
            cmd.Parameters.AddWithValue($"@k{i}", workIds[i]);
        }

        return $"{keyword} w.WorkId IN ({string.Join(",", names)})";
    }
}
