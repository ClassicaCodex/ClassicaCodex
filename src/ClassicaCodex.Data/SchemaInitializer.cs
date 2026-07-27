using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data;

/// <summary>
/// Creates the ClassicaCodex schema on first run. Every statement is
/// idempotent (CREATE ... IF NOT EXISTS, natively supported by SQLite - no
/// more of the sys.tables-existence-check wrapping the SQL Server version
/// needed), so this is safe to call every time the app starts.
///
/// Two things intentionally absent from the SQL Server version of this
/// file, on purpose rather than by oversight:
///
/// 1. Full-Text Search / FORMSOF stemming. That was a SQL Server-only
///    feature to begin with (optional component, wrapped in TRY/CATCH
///    because it wasn't always installed). SQLite's FTS5 could approximate
///    it, but the lemma system + WordIndex already carry the real search
///    workload for Greek/Latin - FTS5 would mostly be replicating English
///    light-stemming that the rest of the app doesn't depend on. Dropped
///    rather than reimplemented.
///
/// 2. The old width-migration statements (ALTER COLUMN to widen a NVARCHAR
///    that turned out too narrow). SQLite is dynamically typed - a TEXT
///    column has no declared maximum length to outgrow in the first place,
///    so the entire class of bug those migrations existed to fix can't
///    happen here.
/// </summary>
public static class SchemaInitializer
{
    public static async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        foreach (var statement in SchemaStatements)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = statement;
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static readonly string[] SchemaStatements =
    {
        @"CREATE TABLE IF NOT EXISTS Authors (
            AuthorId    INTEGER PRIMARY KEY,
            CtsUrn      TEXT NOT NULL,
            Name        TEXT NOT NULL,
            Namespace   TEXT NOT NULL,
            Language    TEXT NULL,
            CONSTRAINT UQ_Authors_CtsUrn UNIQUE (CtsUrn)
        );",

        @"CREATE TABLE IF NOT EXISTS Works (
            WorkId          INTEGER PRIMARY KEY,
            AuthorId        INTEGER NOT NULL,
            CtsUrn          TEXT NOT NULL,
            Title           TEXT NOT NULL,
            CitationScheme  TEXT NULL,
            CONSTRAINT UQ_Works_CtsUrn UNIQUE (CtsUrn),
            CONSTRAINT FK_Works_Authors FOREIGN KEY (AuthorId) REFERENCES Authors(AuthorId)
        );",

        @"CREATE TABLE IF NOT EXISTS Editions (
            EditionId   INTEGER PRIMARY KEY,
            WorkId      INTEGER NOT NULL,
            CtsUrn      TEXT NOT NULL,
            Kind        TEXT NOT NULL,   -- Original / Translation / Unknown
            Language    TEXT NULL,
            Translator  TEXT NULL,
            SourcePath  TEXT NULL,
            CONSTRAINT UQ_Editions_CtsUrn UNIQUE (CtsUrn),
            CONSTRAINT FK_Editions_Works FOREIGN KEY (WorkId) REFERENCES Works(WorkId)
        );",

        // CitationRef is plain TEXT - most works cite by simple numbers
        // ("1.1"), but some Perseus texts (a handful of Aeschines/Demosthenes
        // orations) use whole descriptive phrases as a div's @n attribute
        // instead of a number. TEXT has no declared length limit to hit.
        @"CREATE TABLE IF NOT EXISTS TextNodes (
            TextNodeId  INTEGER PRIMARY KEY,
            EditionId   INTEGER NOT NULL,
            CitationRef TEXT NOT NULL,
            SortOrder   INTEGER NOT NULL,
            Text        TEXT NOT NULL,
            CONSTRAINT FK_TextNodes_Editions FOREIGN KEY (EditionId) REFERENCES Editions(EditionId)
        );",

        @"CREATE INDEX IF NOT EXISTS IX_TextNodes_Edition_Sort ON TextNodes (EditionId, SortOrder);",

        @"CREATE TABLE IF NOT EXISTS Tags (
            TagId    INTEGER PRIMARY KEY,
            Name     TEXT NOT NULL,
            Category TEXT NULL,
            CONSTRAINT UQ_Tags_Name UNIQUE (Name)
        );",

        @"CREATE TABLE IF NOT EXISTS TextNodeTags (
            TextNodeId INTEGER NOT NULL,
            TagId      INTEGER NOT NULL,
            CONSTRAINT PK_TextNodeTags PRIMARY KEY (TextNodeId, TagId),
            CONSTRAINT FK_TextNodeTags_TextNodes FOREIGN KEY (TextNodeId) REFERENCES TextNodes(TextNodeId),
            CONSTRAINT FK_TextNodeTags_Tags FOREIGN KEY (TagId) REFERENCES Tags(TagId)
        );",

        @"CREATE INDEX IF NOT EXISTS IX_Tags_Name ON Tags (Name);",

        // Bookmarks - your own notes pinned to a specific line, e.g. "check
        // this against Ovid's version" or "cf. Norseverse thesis".
        @"CREATE TABLE IF NOT EXISTS Bookmarks (
            BookmarkId INTEGER PRIMARY KEY,
            TextNodeId INTEGER NOT NULL,
            Note       TEXT NULL,
            CreatedAt  TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT FK_Bookmarks_TextNodes FOREIGN KEY (TextNodeId) REFERENCES TextNodes(TextNodeId)
        );",

        @"CREATE INDEX IF NOT EXISTS IX_Bookmarks_TextNodeId ON Bookmarks (TextNodeId);",

        // Lemmas - inflected form to dictionary headword mapping. This is
        // what makes Greek/Latin search and concordance actually work:
        // without it, λόγος/λόγου/λόγῳ/λόγον are four unrelated strings.
        // Deliberately NOT unique on Form - one form can genuinely map to
        // several headwords, and that ambiguity is real, not a data defect.
        @"CREATE TABLE IF NOT EXISTS Lemmas (
            LemmaId        INTEGER PRIMARY KEY,
            Form           TEXT NOT NULL,
            NormalizedForm TEXT NOT NULL,
            Headword       TEXT NOT NULL,
            Language       TEXT NOT NULL,
            PartOfSpeech   TEXT NULL
        );",

        @"CREATE INDEX IF NOT EXISTS IX_Lemmas_NormalizedForm ON Lemmas (Language, NormalizedForm);",
        @"CREATE INDEX IF NOT EXISTS IX_Lemmas_Headword ON Lemmas (Language, Headword);",

        // WordIndex - an inverted index: one row per (normalized word, line).
        // Without it, searching for a lemma means one LIKE '%form%' per
        // attested form, and a leading wildcard can't use an index, so every
        // form costs a full scan of the entire corpus. With it, the same
        // search is an index seek.
        @"CREATE TABLE IF NOT EXISTS WordIndex (
            NormalizedWord TEXT NOT NULL,
            TextNodeId     INTEGER NOT NULL
        );",

        @"CREATE INDEX IF NOT EXISTS IX_WordIndex_Word ON WordIndex (NormalizedWord, TextNodeId);",

        // Definitions - dictionary entries keyed by headword, so Word Study
        // can say what a word actually means rather than only what its
        // dictionary form is.
        @"CREATE TABLE IF NOT EXISTS Definitions (
            DefinitionId       INTEGER PRIMARY KEY,
            Headword           TEXT NOT NULL,
            NormalizedHeadword TEXT NOT NULL,
            Language           TEXT NOT NULL,
            Entry              TEXT NOT NULL,
            Source             TEXT NULL
        );",

        @"CREATE INDEX IF NOT EXISTS IX_Definitions_Normalized ON Definitions (Language, NormalizedHeadword);"
    };
}
