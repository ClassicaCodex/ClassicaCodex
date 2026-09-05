using ClassicaCodex.Core;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

/// <summary>Read-only, pinned to the library selected when the arcade was opened.</summary>
public sealed class ArcadeQuestRepository
{
    public string DatabasePath { get; }
    public ArcadeQuestRepository(string databasePath) => DatabasePath = Path.GetFullPath(databasePath);

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    public List<ArcadeStory> Load(CancellationToken cancellationToken = default)
    {
        using var connection = Open();
        var works = new List<(int Id, string Author, string Title)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT w.WorkId,a.Name,w.Title FROM Works w JOIN Authors a ON a.AuthorId=w.AuthorId ORDER BY w.WorkId";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                works.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
            }
        }
        var resolved = new Dictionary<QuestPassage, IReadOnlyList<ArcadePassage>>();
        foreach (var passage in QuestArcs.All.SelectMany(a => a.Passages).Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = new List<ArcadePassage>();
            foreach (var work in works.Where(w => ArcadeQuest.MatchesWork(passage, w.Author, w.Title)))
            {
                using var command = connection.CreateCommand();
                // First restrict by work, then exact-match the displayed citation in
                // Core. The suffix predicate is only a cheap candidate filter.
                command.CommandText = @"SELECT n.TextNodeId,w.WorkId,a.Name,w.Title,n.CitationRef,n.Text,COALESCE(e.Language,'')
                    FROM TextNodes n JOIN Editions e ON e.EditionId=n.EditionId
                    JOIN Works w ON w.WorkId=e.WorkId JOIN Authors a ON a.AuthorId=w.AuthorId
                    WHERE w.WorkId=@work AND (n.CitationRef=@ref OR n.CitationRef LIKE @suffix)
                    AND COALESCE(n.NodeKind,'line')='line' AND trim(n.Text)<>''
                    ORDER BY CASE WHEN e.Kind='Original' THEN 0 ELSE 1 END,e.EditionId,n.TextNodeId";
                command.Parameters.AddWithValue("@work", work.Id);
                command.Parameters.AddWithValue("@ref", passage.CitationRef);
                command.Parameters.AddWithValue("@suffix", "%" + passage.CitationRef);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = Read(reader);
                    if (ArcadeQuest.MatchesAddress(passage, row)) rows.Add(row);
                }
            }
            resolved[passage] = rows;
        }
        return QuestArcs.PlayableIn(p => resolved[p].Count > 0)
            .Select(a => new ArcadeStory(a, a.Passages.Select(p => resolved[p]).ToArray())).ToList();
    }

    public ArcadePassage? GetPassage(long nodeId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT n.TextNodeId,w.WorkId,a.Name,w.Title,n.CitationRef,n.Text,COALESCE(e.Language,'')
            FROM TextNodes n JOIN Editions e ON e.EditionId=n.EditionId
            JOIN Works w ON w.WorkId=e.WorkId JOIN Authors a ON a.AuthorId=w.AuthorId
            WHERE n.TextNodeId=@id AND COALESCE(n.NodeKind,'line')='line'";
        command.Parameters.AddWithValue("@id", nodeId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>Re-resolves a journal address after re-ingestion; saved numeric IDs are never trusted.</summary>
    public ArcadePassage? FindRememberedPassage(ArcadePassage remembered)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT n.TextNodeId,w.WorkId,a.Name,w.Title,n.CitationRef,n.Text,COALESCE(e.Language,'')
            FROM TextNodes n JOIN Editions e ON e.EditionId=n.EditionId
            JOIN Works w ON w.WorkId=e.WorkId JOIN Authors a ON a.AuthorId=w.AuthorId
            WHERE a.Name=@author COLLATE NOCASE AND w.Title=@title COLLATE NOCASE
            AND (n.CitationRef=@ref OR n.CitationRef LIKE @suffix) AND COALESCE(n.NodeKind,'line')='line'
            ORDER BY CASE WHEN e.Language=@language THEN 0 ELSE 1 END,n.TextNodeId";
        var citation = PassageCitation.Display(remembered.Citation);
        command.Parameters.AddWithValue("@author", remembered.Author); command.Parameters.AddWithValue("@title", remembered.Title);
        command.Parameters.AddWithValue("@language", remembered.Language);
        command.Parameters.AddWithValue("@ref", citation); command.Parameters.AddWithValue("@suffix", "%" + citation);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = Read(reader);
            if (PassageCitation.Display(row.Citation) == citation && !string.IsNullOrWhiteSpace(row.Text)) return row;
        }
        return null;
    }

    public List<BronzeWitness> LoadWitnesses(BronzeEnemyKind creature, CancellationToken cancellationToken = default)
    {
        using var connection = Open();
        var result = new List<BronzeWitness>();
        foreach (var witness in BronzeWitnesses.All.Where(w => w.Creature == creature))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var spec = new QuestPassage(witness.AuthorKey, witness.TitleKeys, witness.Citation, "", "");
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT n.TextNodeId,w.WorkId,a.Name,w.Title,n.CitationRef,n.Text,COALESCE(e.Language,''),e.EditionId
                FROM Authors a JOIN Works w ON w.AuthorId=a.AuthorId JOIN Editions e ON e.WorkId=w.WorkId
                JOIN TextNodes n ON n.EditionId=e.EditionId
                WHERE instr(lower(a.Name),@author)>0 AND (n.CitationRef=@ref OR n.CitationRef LIKE @suffix
                    OR (@section=1 AND (n.CitationRef LIKE @plainChildren OR n.CitationRef LIKE @urnChildren)))
                AND COALESCE(n.NodeKind,'line')='line' AND trim(n.Text)<>''
                ORDER BY CASE WHEN e.Kind='Original' THEN 0 ELSE 1 END,e.EditionId,LENGTH(n.CitationRef),n.CitationRef,n.TextNodeId";
            command.Parameters.AddWithValue("@author", witness.AuthorKey.ToLowerInvariant());
            command.Parameters.AddWithValue("@ref", witness.Citation);
            command.Parameters.AddWithValue("@suffix", "%" + witness.Citation);
            command.Parameters.AddWithValue("@section", witness.Section ? 1 : 0);
            command.Parameters.AddWithValue("@plainChildren", witness.Citation + ".%");
            command.Parameters.AddWithValue("@urnChildren", "%." + witness.Citation + ".%");
            var rows = new List<ArcadePassage>();
            var editions = new HashSet<long>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = Read(reader);
                var reference = PassageCitation.Display(row.Citation);
                // A prose section can be split into numbered child paragraphs.
                // Offer its beginning once per edition; quest gates remain exact.
                if (ArcadeQuest.MatchesWork(spec, row.Author, row.Title)
                    && (reference == witness.Citation || (witness.Section && reference.StartsWith(witness.Citation + ".", StringComparison.Ordinal)))
                    && editions.Add(reader.GetInt64(7))) rows.Add(row);
            }
            if (rows.Count > 0) result.Add(new BronzeWitness(witness, rows));
        }
        return result;
    }

    private static ArcadePassage Read(SqliteDataReader r) =>
        new(r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6));
}
