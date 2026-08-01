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
    /// <summary>
    /// The WordIndex table and its lookup index, exposed rather than kept
    /// private because WordIndexRepository drops and recreates this one
    /// table during a full rebuild. Two hand-maintained copies of the same
    /// CREATE is exactly how a table ends up shaped differently depending on
    /// whether the user ever rebuilt the index - so there is only one.
    /// </summary>
    public const string WordIndexTableDdl =
        "CREATE TABLE IF NOT EXISTS WordIndex (" +
        "NormalizedWord TEXT NOT NULL, " +
        "TextNodeId     INTEGER NOT NULL);";

    /// <inheritdoc cref="WordIndexTableDdl"/>
    public const string WordIndexIndexDdl =
        "CREATE INDEX IF NOT EXISTS IX_WordIndex_Word ON WordIndex (NormalizedWord, TextNodeId);";

    /// <summary>
    /// Bump this whenever a Migrations entry is added. A database file
    /// carries its own version in PRAGMA user_version, so an existing
    /// library gets brought forward on the next launch without the user
    /// doing anything - and without "delete your database and re-ingest"
    /// ever being the release note.
    /// </summary>
    private const int TargetSchemaVersion = 6;

    public static async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        // Whether this file already had a schema decides what happens here: a
        // brand new database is by definition already at the current version
        // and has nothing to migrate, while an existing one may be several
        // versions behind. Checked first, since after the CREATEs below the
        // two are indistinguishable.
        var isNewDatabase = !await TableExistsAsync(conn, "Authors", cancellationToken);

        // Migrations run BEFORE the CREATEs, not after. SchemaStatements
        // describes the current shape, so on an older file it can reference
        // columns that only exist once a migration has added them - the v2
        // index on Bookmarks(EditionId, CitationRef) would fail outright
        // against a v1 Bookmarks table that still keys on TextNodeId. Bring
        // the file up to shape first; the CREATEs then act as a backstop for
        // anything genuinely new that no migration covers.
        if (!isNewDatabase)
        {
            await ApplyMigrationsAsync(conn, cancellationToken);
        }

        foreach (var statement in SchemaStatements)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = statement;
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        if (isNewDatabase)
        {
            await SetSchemaVersionAsync(conn, TargetSchemaVersion, cancellationToken);
        }
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection conn, string tableName, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @Name LIMIT 1;";
        cmd.Parameters.AddWithValue("@Name", tableName);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null && result != DBNull.Value;
    }

    private static async Task<int> GetSchemaVersionAsync(
        SqliteConnection conn, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
    }

    private static async Task SetSchemaVersionAsync(
        SqliteConnection conn, int version, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();

        // PRAGMA values can't be parameterized - this one is a private const
        // int, never anything user-supplied.
        cmd.CommandText = $"PRAGMA user_version = {version};";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Runs every migration the file hasn't seen yet, in order, each in its
    /// own transaction. SQLite makes DDL transactional, so a migration that
    /// fails partway rolls back whole rather than leaving a half-changed
    /// schema and a version number that lies about it.
    /// </summary>
    private static async Task ApplyMigrationsAsync(
        SqliteConnection conn, CancellationToken cancellationToken)
    {
        var currentVersion = await GetSchemaVersionAsync(conn, cancellationToken);
        if (currentVersion >= TargetSchemaVersion) return;

        // Foreign key enforcement goes off for the duration. This is SQLite's
        // own documented procedure for rebuilding a table (create new, copy,
        // drop old, rename): with enforcement on, dropping or renaming a
        // table mid-rebuild can trip constraints against a schema that is
        // only briefly inconsistent. It has to happen out here because the
        // PRAGMA is a no-op inside a transaction - SQLite ignores it unless
        // there's no pending BEGIN.
        await ExecuteAsync(conn, "PRAGMA foreign_keys = OFF;", cancellationToken);

        try
        {
            for (var version = currentVersion + 1; version <= TargetSchemaVersion; version++)
            {
                if (!Migrations.TryGetValue(version, out var statements)) continue;

                await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

                foreach (var statement in statements)
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = (SqliteTransaction)transaction;
                    cmd.CommandText = statement;
                    cmd.CommandTimeout = 300;
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                // Outside the transaction: PRAGMA user_version doesn't
                // participate in one, so writing it inside would survive a
                // rollback and claim a migration that didn't happen.
                await SetSchemaVersionAsync(conn, version, cancellationToken);
            }
        }
        finally
        {
            await ExecuteAsync(conn, "PRAGMA foreign_keys = ON;", cancellationToken);
        }
    }

    private static async Task ExecuteAsync(
        SqliteConnection conn, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Changes that can't be expressed as an idempotent CREATE in
    /// SchemaStatements above - dropping things, altering things, backfilling
    /// things. Keyed by the version they bring the file up to.
    /// </summary>
    private static readonly Dictionary<int, string[]> Migrations = new()
    {
        // v1: IX_Tags_Name was fully redundant with the index SQLite already
        // maintains for the UQ_Tags_Name unique constraint - two B-trees over
        // the same column, both updated on every tag write. Dropping it is
        // pure gain; the unique constraint's own index serves every lookup
        // that one did.
        [1] = new[]
        {
            "DROP INDEX IF EXISTS IX_Tags_Name;"
        },

        // v2: re-key tags and bookmarks from TextNodeId to the passage they
        // actually refer to. See the PassageTags comment above for why.
        //
        // The backfill resolves each old row through the TextNode it pointed
        // at, so an existing library keeps every tag and bookmark it has.
        // Rows whose TextNode has already gone (a re-ingest that got through
        // before the tag was added, say) can't be resolved to a citation and
        // are dropped - they were already dangling and would never have
        // displayed. The INNER JOIN is what drops them, deliberately.
        //
        // SELECT DISTINCT because several TextNodes can share one citation
        // ref, and the new primary key collapses them into a single tag on
        // that passage - which is what "this passage is tagged" meant all
        // along.
        [2] = new[]
        {
            @"CREATE TABLE IF NOT EXISTS PassageTags (
                EditionId   INTEGER NOT NULL,
                CitationRef TEXT NOT NULL,
                TagId       INTEGER NOT NULL,
                CONSTRAINT PK_PassageTags PRIMARY KEY (EditionId, CitationRef, TagId),
                CONSTRAINT FK_PassageTags_Tags FOREIGN KEY (TagId) REFERENCES Tags(TagId)
            );",

            @"INSERT OR IGNORE INTO PassageTags (EditionId, CitationRef, TagId)
              SELECT DISTINCT tn.EditionId, tn.CitationRef, tnt.TagId
              FROM TextNodeTags tnt
              JOIN TextNodes tn ON tnt.TextNodeId = tn.TextNodeId;",

            "DROP TABLE IF EXISTS TextNodeTags;",

            "CREATE INDEX IF NOT EXISTS IX_PassageTags_TagId ON PassageTags (TagId, EditionId, CitationRef);",

            // Bookmarks can't be rebuilt in place - SQLite can't drop or
            // retype a column - so this is the standard create/copy/swap.
            // BookmarkId is carried across so anything holding one still
            // refers to the same note.
            @"CREATE TABLE IF NOT EXISTS Bookmarks_v2 (
                BookmarkId  INTEGER PRIMARY KEY,
                EditionId   INTEGER NOT NULL,
                CitationRef TEXT NOT NULL,
                Note        TEXT NULL,
                CreatedAt   TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );",

            @"INSERT INTO Bookmarks_v2 (BookmarkId, EditionId, CitationRef, Note, CreatedAt)
              SELECT b.BookmarkId, tn.EditionId, tn.CitationRef, b.Note, b.CreatedAt
              FROM Bookmarks b
              JOIN TextNodes tn ON b.TextNodeId = tn.TextNodeId;",

            "DROP TABLE Bookmarks;",

            "ALTER TABLE Bookmarks_v2 RENAME TO Bookmarks;",

            "DROP INDEX IF EXISTS IX_Bookmarks_TextNodeId;",

            "CREATE INDEX IF NOT EXISTS IX_Bookmarks_Passage ON Bookmarks (EditionId, CitationRef);",

            "CREATE INDEX IF NOT EXISTS IX_TextNodes_Edition_Citation ON TextNodes (EditionId, CitationRef);"
        },

        // v3: somewhere to keep each edition's TEI header. Created empty -
        // there is nothing to backfill from, since the information was never
        // stored in the first place. It populates as editions are ingested,
        // and until then the details view falls back to reading the source
        // file the way it always did, so an existing library loses nothing
        // by not re-ingesting immediately.
        [3] = new[]
        {
            @"CREATE TABLE IF NOT EXISTS EditionHeaders (
                EditionId          INTEGER PRIMARY KEY,
                Title              TEXT NULL,
                Author             TEXT NULL,
                Publisher          TEXT NULL,
                PublicationDate    TEXT NULL,
                PublicationPlace   TEXT NULL,
                SourceDescription  TEXT NULL,
                EditionStatement   TEXT NULL,
                Availability       TEXT NULL,
                CONSTRAINT FK_EditionHeaders_Editions FOREIGN KEY (EditionId) REFERENCES Editions(EditionId)
            );",

            @"CREATE TABLE IF NOT EXISTS EditionResponsibilities (
                EditionId INTEGER NOT NULL,
                SortOrder INTEGER NOT NULL,
                Text      TEXT NOT NULL,
                CONSTRAINT PK_EditionResponsibilities PRIMARY KEY (EditionId, SortOrder),
                CONSTRAINT FK_EditionResponsibilities_Editions FOREIGN KEY (EditionId) REFERENCES Editions(EditionId)
            );"
        },

        // v4: created this table under its original name, back when these
        // were searches you saved and managed by hand. Left exactly as it
        // shipped - a database that already ran it has a table called
        // SavedSearches, and rewriting a migration after the fact would
        // leave that file with no path to the current schema. v5 renames it.
        [4] = new[]
        {
            @"CREATE TABLE IF NOT EXISTS SavedSearches (
                SavedSearchId  INTEGER PRIMARY KEY AUTOINCREMENT,
                Name           TEXT NOT NULL,
                Query          TEXT NOT NULL,
                MatchMode      TEXT NOT NULL,
                Languages      TEXT NOT NULL DEFAULT '',
                Corpora        TEXT NOT NULL DEFAULT '',
                OriginalsOnly  INTEGER NULL,
                AuthorName     TEXT NULL,
                TagName        TEXT NULL,
                BookmarkedOnly INTEGER NOT NULL DEFAULT 0,
                EraLabel       TEXT NULL,
                CreatedAt      TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT UQ_SavedSearches_Name UNIQUE (Name)
            );"
        },

        // v5: the same table, renamed to match what it now holds - searches
        // recorded automatically as they are run, rather than ones someone
        // named and filed. Renaming rather than recreating keeps whatever a
        // v4 database already collected.
        //
        // The column keeps the name Name: it holds the search's description
        // now rather than a title someone typed, but the unique constraint
        // on it is doing real work either way. Two runs that describe
        // identically are the same search, so the constraint is what makes
        // re-running one move it up the list instead of filling the list
        // with copies of itself.
        [5] = new[]
        {
            "ALTER TABLE SavedSearches RENAME TO RecentSearches;"
        },

        // v6: give the table the column names the current schema declares.
        //
        // v5 renamed the table and not the column inside it, so a migrated
        // database ended up with SavedSearchId where a freshly created one -
        // which skips migrations entirely and just gets the current DDL -
        // has RecentSearchId. Every query then worked on a new install and
        // failed on a real library.
        //
        // The correction had to come as a new step rather than an edit to
        // v5. A database that already ran v5 is stamped at 5 and the runner
        // only applies versions above the one recorded, so a fix folded into
        // v5 would reach every database except the ones that need it.
        //
        // Rebuilt rather than renamed because both shapes are out there now,
        // and ALTER TABLE RENAME COLUMN fails on the one where the column
        // already has the right name. Copying by the column names the two
        // shapes share sidesteps the question entirely - the id is left out
        // of the copy and simply reassigned, which costs nothing for a list
        // of the last ten searches.
        [6] = new[]
        {
            @"CREATE TABLE RecentSearches_v6 (
                RecentSearchId INTEGER PRIMARY KEY AUTOINCREMENT,
                Name           TEXT NOT NULL,
                Query          TEXT NOT NULL,
                MatchMode      TEXT NOT NULL,
                Languages      TEXT NOT NULL DEFAULT '',
                Corpora        TEXT NOT NULL DEFAULT '',
                OriginalsOnly  INTEGER NULL,
                AuthorName     TEXT NULL,
                TagName        TEXT NULL,
                BookmarkedOnly INTEGER NOT NULL DEFAULT 0,
                EraLabel       TEXT NULL,
                CreatedAt      TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT UQ_RecentSearches_Name UNIQUE (Name)
            );",

            @"INSERT INTO RecentSearches_v6
                (Name, Query, MatchMode, Languages, Corpora, OriginalsOnly,
                 AuthorName, TagName, BookmarkedOnly, EraLabel, CreatedAt)
              SELECT Name, Query, MatchMode, Languages, Corpora, OriginalsOnly,
                     AuthorName, TagName, BookmarkedOnly, EraLabel, CreatedAt
              FROM RecentSearches;",

            "DROP TABLE RecentSearches;",
            "ALTER TABLE RecentSearches_v6 RENAME TO RecentSearches;"
        }
    };

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

        // SQLite doesn't auto-index foreign key columns the way some other
        // engines do - these two carry the load of the app's single most
        // frequent query shape (every work-open calls
        // EditionRepository.GetByWorkAsync, and the library tree/author
        // grouping both walk Works by AuthorId) without one. Harmless at the
        // corpus size this schema was first written against; worth adding
        // now that the library runs several times bigger across Renaissance
        // and First1KGreek editions than it did then. CREATE INDEX IF NOT
        // EXISTS is safe to add any time - it only ever helps read queries,
        // at the cost of a one-time build and a little index upkeep on
        // writes, which ingestion already pays for the indexes above.
        @"CREATE INDEX IF NOT EXISTS IX_Editions_WorkId ON Editions (WorkId);",
        @"CREATE INDEX IF NOT EXISTS IX_Works_AuthorId ON Works (AuthorId);",

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

        // Annotations resolve to live text through (EditionId, CitationRef);
        // without this that join is a scan of TextNodes per tag lookup.
        @"CREATE INDEX IF NOT EXISTS IX_TextNodes_Edition_Citation ON TextNodes (EditionId, CitationRef);",

        @"CREATE TABLE IF NOT EXISTS Tags (
            TagId    INTEGER PRIMARY KEY,
            Name     TEXT NOT NULL,
            Category TEXT NULL,
            CONSTRAINT UQ_Tags_Name UNIQUE (Name)
        );",

        // PassageTags - which passages carry which tag.
        //
        // Keyed on (EditionId, CitationRef), NOT on TextNodeId, and this is
        // the whole point. TextNodeId is an autoincrement surrogate handed
        // out at ingest time: re-ingesting an edition after a Perseus repo
        // update deletes every one of its TextNodes and inserts fresh rows
        // with entirely new ids. Anything keyed to those ids is either
        // destroyed by the re-ingest or blocks it - and this table was
        // keyed to them, with a plain foreign key, which meant the DELETE
        // failed outright on any edition the reader had tagged. Every such
        // edition was quietly recorded as a failed file and skipped, so the
        // texts you'd worked with most were the ones that silently stopped
        // being updated.
        //
        // (EditionId, CitationRef) is the identity Perseus itself uses -
        // "Iliad 1.5" is stable across re-ingests, parser changes, and
        // renumbered ids, because it's what the citation actually means.
        // Editions are upserted by CTS URN and never deleted, so EditionId
        // is stable too.
        //
        // Deliberately NO foreign key to TextNodes. A tag on a passage that
        // isn't currently loaded is dormant, not invalid - it stops showing
        // in queries and comes back untouched if a later ingest restores
        // that citation. That's the durability the old design lacked.
        @"CREATE TABLE IF NOT EXISTS PassageTags (
            EditionId   INTEGER NOT NULL,
            CitationRef TEXT NOT NULL,
            TagId       INTEGER NOT NULL,
            CONSTRAINT PK_PassageTags PRIMARY KEY (EditionId, CitationRef, TagId),
            CONSTRAINT FK_PassageTags_Tags FOREIGN KEY (TagId) REFERENCES Tags(TagId)
        );",

        // Deliberately no index on Tags(Name): UQ_Tags_Name is a UNIQUE
        // constraint, and SQLite already builds an index to enforce it.
        // A second one on the same column costs write time and disk for
        // nothing. (Migration 1 drops the redundant one from older files.)
        //
        // PassageTags' primary key leads with EditionId, which can't be
        // seeked on TagId alone - and every tag-browse query goes the other
        // way: name -> TagId -> which passages carry it. Without this,
        // GetByTagAsync and the Myth Network's edge queries scan the whole
        // junction table. TagId leads; the passage key trails so the index
        // covers the join without touching the table at all.
        @"CREATE INDEX IF NOT EXISTS IX_PassageTags_TagId ON PassageTags (TagId, EditionId, CitationRef);",

        // EditionHeaders - the publication metadata a TEI file states about
        // itself: which printed edition it was digitised from, who edited
        // it, publisher, year, licence.
        //
        // The ingest used to read each file's body and discard its header
        // entirely, so this was only ever available by re-reading the source
        // file at display time. That worked, but it made the details view
        // the one and only thing in the app that still needed the corpus
        // files after ingest - everything else runs off the database alone,
        // and a library whose download folders had been cleaned up would
        // have quietly lost a feature. Storing it at ingest removes that
        // odd dependency and puts this on the same footing as every other
        // fact about an edition.
        @"CREATE TABLE IF NOT EXISTS EditionHeaders (
            EditionId          INTEGER PRIMARY KEY,
            Title              TEXT NULL,
            Author             TEXT NULL,
            Publisher          TEXT NULL,
            PublicationDate    TEXT NULL,
            PublicationPlace   TEXT NULL,
            SourceDescription  TEXT NULL,
            EditionStatement   TEXT NULL,
            Availability       TEXT NULL,
            CONSTRAINT FK_EditionHeaders_Editions FOREIGN KEY (EditionId) REFERENCES Editions(EditionId)
        );",

        // Editors, translators, funders and the rest - a list rather than a
        // column because a file can name any number of them, and because
        // "everything Monro edited" is a question this corpus invites and a
        // joined-up text blob couldn't answer.
        @"CREATE TABLE IF NOT EXISTS EditionResponsibilities (
            EditionId INTEGER NOT NULL,
            SortOrder INTEGER NOT NULL,
            Text      TEXT NOT NULL,
            CONSTRAINT PK_EditionResponsibilities PRIMARY KEY (EditionId, SortOrder),
            CONSTRAINT FK_EditionResponsibilities_Editions FOREIGN KEY (EditionId) REFERENCES Editions(EditionId)
        );",

        // RecentSearches - a named query with its filters, for the searches
        // someone runs repeatedly.
        //
        // Deliberately holds no foreign keys. The author is stored by name
        // and the era by its label, resolved to whatever the library
        // currently contains at the moment the search is loaded - so a saved
        // search survives a corpus being re-ingested into a fresh database,
        // which renumbers every author id. Referential integrity would be
        // the wrong tool here: a search naming an author you no longer have
        // isn't corrupt, it just finds nothing until that corpus is back.
            @"CREATE TABLE IF NOT EXISTS RecentSearches (
                RecentSearchId  INTEGER PRIMARY KEY AUTOINCREMENT,
                Name           TEXT NOT NULL,
                Query          TEXT NOT NULL,
                MatchMode      TEXT NOT NULL,
                Languages      TEXT NOT NULL DEFAULT '',
                Corpora        TEXT NOT NULL DEFAULT '',
                OriginalsOnly  INTEGER NULL,
                AuthorName     TEXT NULL,
                TagName        TEXT NULL,
                BookmarkedOnly INTEGER NOT NULL DEFAULT 0,
                EraLabel       TEXT NULL,
                CreatedAt      TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT UQ_RecentSearches_Name UNIQUE (Name)
            );",

        // Bookmarks - your own notes pinned to a specific passage, e.g.
        // "check this against Ovid's version" or "cf. Norseverse thesis".
        // Keyed the same durable way as PassageTags, and for the same
        // reason - a note you wrote is the least replaceable thing in the
        // database, since the texts can always be downloaded again.
        @"CREATE TABLE IF NOT EXISTS Bookmarks (
            BookmarkId  INTEGER PRIMARY KEY,
            EditionId   INTEGER NOT NULL,
            CitationRef TEXT NOT NULL,
            Note        TEXT NULL,
            CreatedAt   TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );",

        @"CREATE INDEX IF NOT EXISTS IX_Bookmarks_Passage ON Bookmarks (EditionId, CitationRef);",

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
        WordIndexTableDdl,

        WordIndexIndexDdl,

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

        @"CREATE INDEX IF NOT EXISTS IX_Definitions_Normalized ON Definitions (Language, NormalizedHeadword);",

        // Art & Archaeology objects - vases, coins, gems, sculptures, sites,
        // buildings. Re-ingested wholesale each time the setup step runs
        // (DELETE + re-INSERT, not an incremental upsert), the same "always
        // rebuilds from scratch" choice WordIndex already makes - simpler
        // and safer for a downloaded reference dataset than reconciling
        // diffs against Perseus's own updates.
        @"CREATE TABLE IF NOT EXISTS Artifacts (
            ArtifactId       TEXT PRIMARY KEY,
            Type             TEXT NOT NULL,
            Name             TEXT NULL,
            Region           TEXT NULL,
            Context          TEXT NULL,
            MatchedPlaceName TEXT NULL,
            Period           TEXT NULL,
            StartDate        TEXT NULL,
            EndDate          TEXT NULL,
            Collection       TEXT NULL,
            Material         TEXT NULL,
            Location         TEXT NULL,
            Description      TEXT NULL,
            PrimaryCitation  TEXT NULL
        );",

        @"CREATE INDEX IF NOT EXISTS IX_Artifacts_MatchedPlaceName ON Artifacts (MatchedPlaceName);",

        // Perseus's own image metadata is a three-hop join (artifact -> one
        // or more image ids -> caption/credits for that photo), collapsed
        // here into one row per artifact-image pair at ingest time so the
        // Places Map can query it directly without redoing the joins live.
        @"CREATE TABLE IF NOT EXISTS ArtifactImages (
            ArtifactId TEXT NOT NULL,
            ImageId    TEXT NOT NULL,
            Caption    TEXT NULL,
            Credits    TEXT NULL,
            CONSTRAINT PK_ArtifactImages PRIMARY KEY (ArtifactId, ImageId)
        );",

        @"CREATE INDEX IF NOT EXISTS IX_ArtifactImages_ArtifactId ON ArtifactImages (ArtifactId);"
    };
}
