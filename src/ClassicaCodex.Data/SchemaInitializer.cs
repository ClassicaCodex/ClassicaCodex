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
        "TextNodeId     INTEGER NOT NULL, " +
        "PRIMARY KEY (NormalizedWord, TextNodeId)) WITHOUT ROWID;";

    // There is no WordIndexIndexDdl any more. The table used to be an ordinary
    // rowid table with a covering index over both of its columns; every query
    // used the index and the base table was never read, so the whole index was
    // stored twice - once as the table's own B-tree and once as the index's -
    // and the build paid to fill both.
    //
    // Making the pair the primary key of a WITHOUT ROWID table stores it once.
    // It is a legitimate primary key because WordIndexService.TokenizeLine
    // already takes Distinct() words per line, so (word, line) was unique
    // before this made it so.
    //
    // Measured over a full Perseus library - 26,723,817 entries from 1,085,843
    // lines:
    //
    //                                     build      on disk    10 lookups
    //   rowid table + covering index      263.7 s    1,054 MB      18.2 ms
    //   WITHOUT ROWID                     273.9 s      471 MB      17.8 ms
    //   WITHOUT ROWID, inserted in order  223.0 s      474 MB      16.2 ms
    //
    // 55% smaller, and faster to build once the rows go in near key order,
    // which is what the sort in WordIndexRepository.BulkInsertAsync is for.
    // The word index was the largest single object in a finished library -
    // bigger than every text, dictionary and apparatus entry together.

    /// <summary>
    /// Bump this whenever a Migrations entry is added. A database file
    /// carries its own version in PRAGMA user_version, so an existing
    /// library gets brought forward on the next launch without the user
    /// doing anything - and without "delete your database and re-ingest"
    /// ever being the release note.
    ///
    /// Public so the tests can assert against this number rather than a copy
    /// of it. Nine tests hardcoded 6 and went on passing through migrations
    /// 7 to 13 until version 3 was cut, at which point they all failed at
    /// once and said nothing about what had actually broken.
    /// </summary>
    public const int TargetSchemaVersion = 36;

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
        SqliteConnection conn, int version, CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;

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

                // Inside the transaction, deliberately. user_version lives in the
                // database header page, which is journalled like any other page - it
                // rolls back with everything else, so stamping it here cannot claim a
                // migration that did not happen. Writing it after the commit instead
                // leaves a window where the schema has moved and the version has not,
                // and ten of these migrations cannot be replayed: the next launch dies
                // on "duplicate column name" and the library stops opening at all.
                await SetSchemaVersionAsync(conn, version, cancellationToken,
                    (SqliteTransaction)transaction);

                await transaction.CommitAsync(cancellationToken);
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
        },

        // v7: favourite works.
        //
        // Keyed on the work's CTS URN rather than its WorkId. Ids are
        // assigned locally and renumber when a corpus is re-ingested into a
        // fresh file, so a favourites list keyed on them would silently come
        // back pointing at different works - which is worse than losing it,
        // because nothing about it would look wrong.
        //
        // A plain CREATE with no backfill: nothing existed to carry forward,
        // and a new table is the one migration shape that cannot half-apply
        // to an existing library.
        [7] = new[]
        {
            @"CREATE TABLE IF NOT EXISTS FavoriteWorks (
                CtsUrn    TEXT NOT NULL PRIMARY KEY,
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );"
        },

        // v8: saved stylometric runs.
        //
        // A single Delta run is close to uninterpretable alone - every useful
        // reading comes from comparing runs, either across works or across
        // preprocessing settings for the same work. Doing that by holding
        // screenshots side by side is how a run gets compared against itself
        // without anyone noticing.
        //
        // Three tables: the run and its settings, the full neighbour list, and
        // the word-frequency fingerprint.
        //
        // Author and title are DENORMALISED into the results rather than
        // joined at read time. WorkIds are assigned locally and renumber on a
        // re-ingest, so a run joined live would come back describing different
        // works while still looking valid. A saved run is a historical record
        // of what the analysis said on a particular day, not a live view - it
        // should stay readable even if the corpus underneath it is replaced.
        //
        // The full neighbour list is stored, not just the top 20 the UI shows.
        // A few thousand rows per run is nothing, and truncating would rule out
        // the reference-distribution work (where in a work's own ranking does
        // the first other author appear?) which is the entire reason for this.
        //
        // AlgorithmVersion exists because ComputeDelta will change again. It
        // already changed once mid-analysis - de-duplicating the pool moved
        // every figure - and runs from either side of that change are not
        // comparable. Without a recorded version they would look comparable,
        // which is the failure mode worth spending a column to avoid.
        //
        // PoolSize serves the same purpose for corpus growth: Delta z-scores
        // are relative to the pool, so runs against pools of different sizes
        // need at minimum a warning before being charted together.
        [8] = new[]
        {
            @"CREATE TABLE IF NOT EXISTS ApparatusEntries (
            ApparatusId INTEGER PRIMARY KEY AUTOINCREMENT,
            EditionId   INTEGER NOT NULL,
            CitationRef TEXT    NOT NULL,
            SortOrder   INTEGER NOT NULL,
            Kind        TEXT    NOT NULL,
            Lemma       TEXT    NULL,
            Witness     TEXT    NULL,
            Content     TEXT    NOT NULL,
            CONSTRAINT FK_ApparatusEntries_Editions
                FOREIGN KEY (EditionId) REFERENCES Editions(EditionId)
        );",

        @"CREATE INDEX IF NOT EXISTS IX_ApparatusEntries_Line
            ON ApparatusEntries (EditionId, CitationRef, SortOrder);",

        @"CREATE TABLE IF NOT EXISTS StylometryRuns (
                RunId             INTEGER PRIMARY KEY AUTOINCREMENT,
                CreatedUtc        TEXT    NOT NULL,
                TargetWorkId      INTEGER NOT NULL,
                TargetEditionId   INTEGER NOT NULL,
                TargetAuthorName  TEXT    NOT NULL,
                TargetWorkTitle   TEXT    NOT NULL,
                Language          TEXT    NOT NULL,
                FeatureWordCount  INTEGER NOT NULL,
                FoldAccents       INTEGER NOT NULL,
                StripElisionMarks INTEGER NOT NULL,
                PoolSize          INTEGER NOT NULL,
                AlgorithmVersion  INTEGER NOT NULL,
                Label             TEXT    NULL,
                Notes             TEXT    NULL
            );",

            @"CREATE INDEX IF NOT EXISTS IX_StylometryRuns_Target
                ON StylometryRuns (TargetWorkId, AlgorithmVersion, FeatureWordCount, FoldAccents);",

            @"CREATE INDEX IF NOT EXISTS IX_StylometryRuns_Settings
                ON StylometryRuns (Language, AlgorithmVersion, FeatureWordCount, FoldAccents, StripElisionMarks);",

            @"CREATE TABLE IF NOT EXISTS StylometryRunResults (
                RunId       INTEGER NOT NULL,
                Rank        INTEGER NOT NULL,
                WorkId      INTEGER NOT NULL,
                AuthorName  TEXT    NOT NULL,
                WorkTitle   TEXT    NOT NULL,
                Delta       REAL    NOT NULL,
                CONSTRAINT PK_StylometryRunResults PRIMARY KEY (RunId, Rank),
                CONSTRAINT FK_StylometryRunResults_Runs
                    FOREIGN KEY (RunId) REFERENCES StylometryRuns(RunId) ON DELETE CASCADE
            );",

            @"CREATE INDEX IF NOT EXISTS IX_StylometryRunResults_Author
                ON StylometryRunResults (RunId, AuthorName, Rank);",

            @"CREATE TABLE IF NOT EXISTS StylometryRunFeatures (
                RunId             INTEGER NOT NULL,
                Rank              INTEGER NOT NULL,
                Word              TEXT    NOT NULL,
                RelativeFrequency REAL    NOT NULL,
                CONSTRAINT PK_StylometryRunFeatures PRIMARY KEY (RunId, Rank),
                CONSTRAINT FK_StylometryRunFeatures_Runs
                    FOREIGN KEY (RunId) REFERENCES StylometryRuns(RunId) ON DELETE CASCADE
            );"
        },

        // v9: token count of the target text.
        //
        // Added to test a confound in the depth-to-first-outsider measure.
        // Across the Euripides corpus the works with the shallowest depth are
        // also, with one exception, the shortest surviving plays. Shorter texts
        // give noisier relative-frequency estimates, which inflates Delta
        // against everything and lets works by other authors rise in the
        // ranking earlier - producing exactly the pattern that would otherwise
        // be read as weak authorial signal.
        //
        // Until depth is regressed against length, a shallow depth cannot be
        // attributed to authorship at all. Nullable because runs saved before
        // this migration have no count and must be excluded from the
        // regression rather than silently treated as zero.
        [9] = new[]
        {
            @"ALTER TABLE StylometryRuns ADD COLUMN TargetTokenCount INTEGER NULL;"
        },

        // v10: sample size.
        //
        // Chunking splits each work into fixed-size token samples so that every
        // comparison unit is the same length - the only way to ask an
        // authorship question on a corpus where depth to first outsider tracks
        // length instead.
        //
        // The column exists because sample size was initially left out of the
        // settings record, and runs at different sample sizes therefore shared
        // a settings profile. The analysis form pooled a chunked batch with an
        // unchunked one and presented the mixture as a single reference
        // distribution: same works appearing twice at slightly different
        // depths, with nothing on screen to indicate why.
        //
        // Two runs are only comparable if every preprocessing decision behind
        // them matches. Any such decision that is not recorded here will
        // eventually be silently mixed, and the failure looks like noise rather
        // than like a bug.
        //
        // 0 means whole works. Existing rows predate chunking and are
        // backfilled to 0, which is what they were.
        [10] = new[]
        {
            @"ALTER TABLE StylometryRuns ADD COLUMN ChunkSize INTEGER NOT NULL DEFAULT 0;"
        },

        // v11: athetized lines.
        //
        // TEI <del> in a printed critical edition marks a line the editor
        // believes is interpolated - transmitted in the manuscripts, printed
        // in square brackets, but doubted. Fourteen lines of Agamemnon alone
        // are encoded this way.
        //
        // The parser previously discarded <del> content entirely, on the
        // reading that <del> means "deleted". In a manuscript transcription it
        // does; in a printed edition it means "athetized", and the text is
        // still the text. Agamemnon 7 came through as a bare full stop.
        //
        // With the content restored, the remaining problem is that an
        // athetized line now looks identical to an accepted one, which no
        // printed edition would do. This column carries the distinction so the
        // reader can show it.
        //
        // A flag rather than brackets in the text itself: Text is tokenised,
        // searched, exported and fed to the stylometry, and punctuation
        // injected for display would end up in all four.
        //
        // Existing rows default to 0. They were ingested before the flag
        // existed and a re-ingest is needed to populate it - which the <del>
        // fix required anyway.
        [11] = new[]
        {
            @"ALTER TABLE TextNodes ADD COLUMN IsAthetized INTEGER NOT NULL DEFAULT 0;"
        },

        // v12: the critical apparatus.
        //
        // The parser excludes <app>, <rdg> and <note> from the reading text,
        // and must: they are commentary about the text, and counting an
        // editor's surname as a Greek word skews everything downstream.
        //
        // But that material IS the scholarship. It records which manuscripts
        // read what, who conjectured what, and why a line is doubted. A
        // printed edition puts it in small type at the foot of the page. Until
        // now this application threw it away entirely, so a reader could see
        // that line 7 of Agamemnon was bracketed but had no way to learn that
        // Pauw bracketed it.
        //
        // Stored separately rather than inline for the same reason it is
        // excluded from Text: anything in TextNodes.Text is tokenised,
        // searched, exported and fed to the stylometry.
        //
        // Keyed by (EditionId, CitationRef) rather than TextNodeId because
        // apparatus is attached during parsing, before the text nodes have
        // been assigned ids, and because a re-ingest renumbers those ids while
        // citation references are stable.
        [12] = new[]
        {
            @"CREATE TABLE IF NOT EXISTS ApparatusEntries (
                ApparatusId INTEGER PRIMARY KEY AUTOINCREMENT,
                EditionId   INTEGER NOT NULL,
                CitationRef TEXT    NOT NULL,
                SortOrder   INTEGER NOT NULL,
                Kind        TEXT    NOT NULL,
                Lemma       TEXT    NULL,
                Witness     TEXT    NULL,
                Content     TEXT    NOT NULL,
                CONSTRAINT FK_ApparatusEntries_Editions
                    FOREIGN KEY (EditionId) REFERENCES Editions(EditionId)
            );",

            @"CREATE INDEX IF NOT EXISTS IX_ApparatusEntries_Line
                ON ApparatusEntries (EditionId, CitationRef, SortOrder);"
        },

        // v13: the orthographic level of an edition's text.
        //
        // Menota manuscripts ingest as ordinary Original editions, which puts
        // them in the same pool as Greek and Latin the moment
        // GetAllOriginalEditionsAsync runs. They must not be there. They are
        // diplomatic transcriptions - the spelling follows each scribe rather
        // than a dictionary - so Delta between two of them measures scribal
        // habit, and no amount of sample-size correction fixes that. The
        // failure would be invisible: the numbers come out looking exactly
        // like author distances.
        //
        // A column recording what the text is, rather than a flag saying where
        // it may not be used. A normalised Menota text ingested later is
        // genuinely comparable and needs no special case to become so.
        //
        // Existing rows are NULL, which the stylometry pool reads as
        // editorially normalised - correct for every printed edition already
        // ingested, where an editor has regularised the orthography and the
        // Menota levels do not apply.
        [13] = new[]
        {
            @"ALTER TABLE Editions ADD COLUMN Orthography TEXT NULL;"
        },

        // v14: what sort of thing each text node is.
        //
        // TextNodes holds more than the author's words, and has since the
        // parser learned to stop dropping headings, cast lists and stage
        // directions. Everything in it belongs on the page - a play without
        // its speakers is unreadable - but not all of it is language the
        // author wrote, and every word in Text is tokenised, counted and fed
        // to Burrows's Delta.
        //
        // Nothing filtered by anything, because there was nothing to filter
        // by. Measured across the corpora before this column existed:
        // headings, cast entries and stage directions were 0.5% of the word
        // stream overall but 6.8% of Holinshed's first history and 3-5% of
        // the Terence translations. Adding the 42,448 dropped speaker
        // attributions in the Greek alone would have pushed a play towards
        // 8%, with "ΣΩ." and "Ham." among its most frequent tokens.
        //
        // Defaulting to 'line' is what makes this safe on an existing
        // library: every row already there was reading text as far as
        // anything downstream was concerned, and stays counted. Only a
        // re-ingest labels the rest, and until then the behaviour is exactly
        // what it was.
        [14] = new[]
        {
            @"ALTER TABLE TextNodes ADD COLUMN NodeKind TEXT NOT NULL DEFAULT 'line';"
        },

        // v15: saved validation-bench experiments.
        //
        // The bench produces runs that cannot be re-derived from a Delta run:
        // a leave-one-out sweep is nineteen validations, a parameter grid is
        // forty, a perturbation series is thousands of synthetic mixtures. Up
        // to now each existed only as a CSV somebody remembered to export, and
        // several conclusions in docs/stylometry-notes.md rest on runs that
        // are gone.
        //
        // WHY A SEPARATE TABLE RATHER THAN MORE COLUMNS ON StylometryRuns.
        // A Delta run has one target and one neighbour list. These have a
        // target AUTHOR, a donor set, a level series, a seed and an iteration
        // count, and produce one row per work per level per donor. Bolting
        // that onto a table shaped for the other thing would leave most
        // columns null in most rows and force the analysis form to branch on
        // which kind of row it was reading.
        //
        // ExperimentKind carries what sort of run it was, so the next
        // experiment type is a new value rather than a new table. Parameters
        // and Metrics are JSON for the same reason: every experiment type has
        // different ones, and a column per parameter would mean a migration
        // per experiment type. The columns that ARE broken out - seed,
        // iterations, sample size, feature count, accent folding - are the
        // ones a query needs to group by, and they are exactly the settings
        // that make two runs incomparable when they differ.
        //
        // Seed is NOT NULL and has no default. An experiment whose seed was not
        // recorded cannot be rebuilt, and a row that silently claimed seed 0
        // would be worse than one that refused to be written.
        [15] = new[]
        {
            @"CREATE TABLE IF NOT EXISTS StylometryExperiments (
                ExperimentId    INTEGER PRIMARY KEY,
                CreatedUtc      TEXT    NOT NULL,
                Kind            TEXT    NOT NULL,
                TargetAuthor    TEXT    NOT NULL,
                Language        TEXT    NULL,
                PoolSummary     TEXT    NOT NULL,
                PoolWorkIds     TEXT    NOT NULL,
                Seed            INTEGER NOT NULL,
                Iterations      INTEGER NOT NULL,
                ChunkSize       INTEGER NOT NULL,
                FeatureWordCount INTEGER NOT NULL,
                FoldAccents     INTEGER NOT NULL,
                AlgorithmVersion INTEGER NOT NULL,
                Parameters      TEXT    NOT NULL,
                Metrics         TEXT    NOT NULL,
                Label           TEXT    NULL,
                Notes           TEXT    NULL
            );",

            @"CREATE INDEX IF NOT EXISTS IX_StylometryExperiments_Kind
                ON StylometryExperiments (Kind, TargetAuthor, CreatedUtc);",

            // One row per work per level per donor. Wide enough to rebuild the
            // table the form showed, and to recompute the cross-work fit and
            // the detection power without re-running anything - which matters,
            // because the detection power was computed against the wrong
            // scatter once already and the fix had to be checked against a
            // stored run rather than a memory of one.
            @"CREATE TABLE IF NOT EXISTS StylometryExperimentRows (
                ExperimentId    INTEGER NOT NULL,
                RowIndex        INTEGER NOT NULL,
                WorkId          INTEGER NULL,
                WorkTitle       TEXT    NOT NULL,
                Donor           TEXT    NOT NULL,
                Level           REAL    NOT NULL,
                MeanMargin      REAL    NOT NULL,
                StdDev          REAL    NOT NULL,
                BaselineMargin  REAL    NOT NULL,
                Recovered       INTEGER NOT NULL,
                Trials          INTEGER NOT NULL,
                NearestAuthor   TEXT    NULL,
                TokenCount      INTEGER NOT NULL,
                PRIMARY KEY (ExperimentId, RowIndex),
                FOREIGN KEY (ExperimentId) REFERENCES StylometryExperiments(ExperimentId) ON DELETE CASCADE
            );"
        },

        // v16: how many mixtures agreed on the nearest author.
        //
        // v15 stored the nearest author's name and not the count, and a
        // reloaded experiment therefore showed "Euripides" where the run had
        // shown "Euripides (14/25)". That count is not decoration: it is how
        // Rhesus was seen to flip to Sophocles in fourteen mixtures out of
        // twenty-five, and how Heracleidae showed nine of twenty-five leaving
        // Euripides at one percent injection. It was the first signal in the
        // whole investigation to move at all.
        //
        // Defaulting to 0 rather than to Trials: rows written under v15 do not
        // know the count, and claiming unanimity for them would be inventing
        // data. Zero displays as a bare author name, which is what those rows
        // honestly are.
        [16] = new[]
        {
            @"ALTER TABLE StylometryExperimentRows ADD COLUMN NearestCount INTEGER NOT NULL DEFAULT 0;"
        },

        // v17: how securely a work is attributed to the author it is filed under.
        //
        // Perseus and First1KGreek file the spuria under the author without
        // comment - correctly, since their job is to transmit what the
        // manuscripts say rather than to adjudicate - so the corpus offers no
        // signal and the library was presenting Definitiones as flatly Platonic.
        //
        // AttributionSetByUser is the column that makes this safe to default
        // from a built-in catalog. Without it, growing the catalog or
        // re-ingesting a corpus would silently overwrite a judgement somebody
        // made deliberately, and the person whose library it is would have no
        // way to make a decision stick.
        [17] = new[]
        {
            @"ALTER TABLE Works ADD COLUMN AttributionStatus TEXT NOT NULL DEFAULT 'accepted';",
            @"ALTER TABLE Works ADD COLUMN AttributionNote TEXT NULL;",
            @"ALTER TABLE Works ADD COLUMN AttributionSetByUser INTEGER NOT NULL DEFAULT 0;"
        },

        // v18: the offline-first Research Bench. Projects belong to stable
        // Works rows; questions and evidence are wholly owned by a project.
        // Evidence may outlive a deleted question, so that link becomes NULL.
        [18] = new[]
        {
            // Both constants are used as they stand, not back-shaped to their v18
            // form. Migration 31 rebuilds both tables wholesale from named columns that
            // exist in either shape, so a legacy file arriving here early with the v31
            // columns ends up in exactly the same place - and no fragile .Replace chain
            // sits here waiting to silently stop matching after a whitespace edit.
            ResearchProjectsDdl,
            ResearchQuestionsDdl,
            EvidenceItemsDdl,
            "CREATE INDEX IF NOT EXISTS IX_ResearchProjects_Work ON ResearchProjects (WorkId, Status, UpdatedUtc);",
            "CREATE INDEX IF NOT EXISTS IX_ResearchQuestions_Project ON ResearchQuestions (ResearchProjectId, SortOrder);",
            "CREATE INDEX IF NOT EXISTS IX_EvidenceItems_Project ON EvidenceItems (ResearchProjectId, SortOrder);",
            "CREATE INDEX IF NOT EXISTS IX_EvidenceItems_Question ON EvidenceItems (ResearchQuestionId);"
        },

        // v19: append-only research history. Related rows may be removed while
        // the human-readable log remains, so those links become NULL.
        [19] = new[]
        {
            ResearchLogEntriesDdl,
            "CREATE INDEX IF NOT EXISTS IX_ResearchLogEntries_Project ON ResearchLogEntries (ResearchProjectId, CreatedUtc, ResearchLogEntryId);"
        },

        // v20: generated evidence keeps raw corpus material separate from an
        // app/AI interpretation and records who/what produced that candidate.
        // A companion table makes this additive and leaves every existing
        // manual EvidenceItems row valid without a backfill.
        [20] = new[]
        {
            EvidenceGenerationMetadataDdl,
            "CREATE INDEX IF NOT EXISTS IX_EvidenceGenerationMetadata_Origin ON EvidenceGenerationMetadata (Origin);"
        },

        // v21: propositions attributed to scholarship are not themselves raw
        // evidence. Keeping them in a claims matrix preserves the source,
        // stance, exact locator and the researcher's verification separately.
        [21] = new[]
        {
            ScholarlyClaimsDdl,
            "CREATE INDEX IF NOT EXISTS IX_ScholarlyClaims_Project ON ScholarlyClaims (ResearchProjectId, SortOrder, ScholarlyClaimId);",
            "CREATE INDEX IF NOT EXISTS IX_ScholarlyClaims_Question ON ScholarlyClaims (ResearchQuestionId);",
            "CREATE INDEX IF NOT EXISTS IX_ScholarlyClaims_Source ON ScholarlyClaims (SourceEvidenceItemId);"
        },

        // v22: local source files stay outside SQLite, while their absolute
        // path, size and SHA-256 fingerprint make replacement or disappearance
        // visible. Page annotations belong to that exact fingerprinted file.
        [22] = new[]
        {
            EvidenceAttachmentsDdl,
            EvidencePageAnnotationsDdl,
            "CREATE UNIQUE INDEX IF NOT EXISTS UX_EvidenceAttachments_Path ON EvidenceAttachments (EvidenceItemId, FilePath);",
            "CREATE INDEX IF NOT EXISTS IX_EvidencePageAnnotations_Attachment ON EvidencePageAnnotations (EvidenceAttachmentId, PageNumber, EvidencePageAnnotationId);"
        },

        // v23: retain imported citation fields instead of flattening them
        // irreversibly into display text, enabling offline RIS/BibTeX export.
        [23] = new[]
        {
            EvidenceBibliographyMetadataDdl
        },

        // v24: reproducibility snapshots freeze stable corpus identities,
        // attribution judgments, edition metadata and ordered-text hashes.
        [24] = new[]
        {
            ResearchCorpusSnapshotsDdl,
            ResearchCorpusSnapshotEntriesDdl,
            "CREATE INDEX IF NOT EXISTS IX_ResearchCorpusSnapshots_Project ON ResearchCorpusSnapshots (ResearchProjectId, CreatedUtc);",
            "CREATE INDEX IF NOT EXISTS IX_ResearchCorpusSnapshotEntries_Snapshot ON ResearchCorpusSnapshotEntries (ResearchCorpusSnapshotId, WorkCtsUrn, EditionCtsUrn);"
        },

        // v25: the reading queue is intentionally upstream of evidence.
        // Stable passage/source references survive re-ingest; promotion is
        // explicit and leaves an auditable link to the resulting evidence.
        [25] = new[]
        {
            ResearchReadingItemsDdl,
            "CREATE INDEX IF NOT EXISTS IX_ResearchReadingItems_Project ON ResearchReadingItems (ResearchProjectId, Status, Priority, SortOrder, ResearchReadingItemId);",
            "CREATE INDEX IF NOT EXISTS IX_ResearchReadingItems_Question ON ResearchReadingItems (ResearchQuestionId);"
        },

        // v26: findings make synthesis an explicit researcher-owned layer.
        // AI text is retained as a candidate beside, never in place of, the
        // researcher's conclusion. Evidence links state their own role.
        [26] = new[]
        {
            ResearchFindingsDdl,
            ResearchFindingEvidenceDdl,
            "CREATE INDEX IF NOT EXISTS IX_ResearchFindings_Project ON ResearchFindings (ResearchProjectId, SortOrder, ResearchFindingId);",
            "CREATE INDEX IF NOT EXISTS IX_ResearchFindings_Question ON ResearchFindings (ResearchQuestionId);",
            "CREATE INDEX IF NOT EXISTS IX_ResearchFindingEvidence_Evidence ON ResearchFindingEvidence (EvidenceItemId);"
        },

        // v27: preserve echo searches as auditable investigations. Passage
        // CTS identities remain authoritative across re-ingest; transient
        // row ids are retained only as navigation hints and are not FKs.
        [27] = new[]
        {
            ResearchEchoInvestigationsDdl.Replace("        SourceLanguage TEXT NULL,", ""),
            ResearchEchoResultsDdl
                .Replace("        TargetLanguage TEXT NULL,", "")
                .Replace("        ConnectionType TEXT NOT NULL DEFAULT 'unclassified',", "")
                .Replace("        Directionality TEXT NOT NULL DEFAULT 'unknown',", "")
                .Replace("        MotifTags TEXT NULL,", "")
                .Replace("        ParallelNote TEXT NULL,", ""),
            "CREATE INDEX IF NOT EXISTS IX_ResearchEchoInvestigations_Project ON ResearchEchoInvestigations (ResearchProjectId, CreatedUtc, ResearchEchoInvestigationId);",
            "CREATE INDEX IF NOT EXISTS IX_ResearchEchoResults_Investigation ON ResearchEchoResults (ResearchEchoInvestigationId, Disposition, SortOrder, ResearchEchoResultId);",
            "CREATE INDEX IF NOT EXISTS IX_ResearchEchoResults_Evidence ON ResearchEchoResults (EvidenceItemId);"
        },

        // v28: a close-reading layer over saved passage pairs. Human
        // classifications live on the result; AI readings are append-only
        // records so a later run never silently replaces an earlier one.
        [28] = new[]
        {
            "ALTER TABLE ResearchEchoInvestigations ADD COLUMN SourceLanguage TEXT NULL;",
            "ALTER TABLE ResearchEchoResults ADD COLUMN TargetLanguage TEXT NULL;",
            "ALTER TABLE ResearchEchoResults ADD COLUMN ConnectionType TEXT NOT NULL DEFAULT 'unclassified';",
            "ALTER TABLE ResearchEchoResults ADD COLUMN Directionality TEXT NOT NULL DEFAULT 'unknown';",
            "ALTER TABLE ResearchEchoResults ADD COLUMN MotifTags TEXT NULL;",
            "ALTER TABLE ResearchEchoResults ADD COLUMN ParallelNote TEXT NULL;",
            ResearchEchoParallelAnalysesDdl,
            "CREATE INDEX IF NOT EXISTS IX_ResearchEchoParallelAnalyses_Result ON ResearchEchoParallelAnalyses (ResearchEchoResultId, CreatedUtc, ResearchEchoParallelAnalysisId);"
        },

        // v29: competing explanations, explicit source-to-hypothesis assessments,
        // and falsification experiments. AI provenance belongs to an accepted
        // proposal, while the assessment matrix remains wholly researcher-owned.
        [29] = new[]
        {
            ResearchHypothesesDdl,
            ResearchHypothesisAssessmentsDdl,
            ResearchExperimentsDdl,
            "CREATE INDEX IF NOT EXISTS IX_ResearchHypotheses_Project ON ResearchHypotheses (ResearchProjectId, SortOrder, ResearchHypothesisId);",
            "CREATE INDEX IF NOT EXISTS IX_ResearchHypothesisAssessments_Hypothesis ON ResearchHypothesisAssessments (ResearchHypothesisId, SourceKind, SourceId);",
            "CREATE INDEX IF NOT EXISTS IX_ResearchExperiments_Project ON ResearchExperiments (ResearchProjectId, Status, SortOrder, ResearchExperimentId);"
        },

        // v30: a passage-first inquiry is intentionally smaller than a
        // Research Bench project. It preserves the reader's own observation
        // and question by stable CTS identity, then optionally records the
        // project into which that note was promoted.
        [30] = new[]
        {
            PassageInquiriesDdl,
            "CREATE UNIQUE INDEX IF NOT EXISTS UX_PassageInquiries_Passage ON PassageInquiries (EditionCtsUrn, CitationRef);",
            "CREATE INDEX IF NOT EXISTS IX_PassageInquiries_Project ON PassageInquiries (ResearchProjectId);"
        },

        // A project outlives its work.
        //
        // WorkId was NOT NULL with a plain FK, which made deleting a Work fail
        // outright once any project referenced it - and Menota re-ingest deletes
        // works when their last edition is replaced. The re-import then aborted
        // part-way with a raw constraint error and no way for the researcher to
        // clear it, since the Bench offers archiving rather than deletion.
        //
        // Rebuilt rather than altered: SQLite cannot change a column's nullability
        // or its ON DELETE action in place. This is SQLite's documented rebuild
        // procedure, and it is safe here only because ApplyMigrationsAsync turns
        // foreign key enforcement off around migrations - with it on, DROP TABLE
        // performs an implicit DELETE FROM and would cascade every question, every
        // evidence item and the whole research log out of existence.
        //
        // WorkCtsUrn rides along so an orphaned project can find its work again
        // after re-ingest: durable identity, the same principle the passage
        // inquiries already use.
        [31] = new[]
        {
            @"CREATE TABLE ResearchProjects_v31 (
                ResearchProjectId INTEGER PRIMARY KEY,
                WorkId INTEGER NULL,
                WorkCtsUrn TEXT NULL,
                Name TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'active',
                Notes TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                CONSTRAINT FK_ResearchProjects_Works FOREIGN KEY (WorkId)
                    REFERENCES Works(WorkId) ON DELETE SET NULL
            );",
            @"INSERT INTO ResearchProjects_v31
                (ResearchProjectId, WorkId, WorkCtsUrn, Name, Status, Notes, CreatedUtc, UpdatedUtc)
              SELECT p.ResearchProjectId, p.WorkId,
                     (SELECT w.CtsUrn FROM Works w WHERE w.WorkId = p.WorkId),
                     p.Name, p.Status, p.Notes, p.CreatedUtc, p.UpdatedUtc
              FROM ResearchProjects p;",
            "DROP TABLE ResearchProjects;",
            "ALTER TABLE ResearchProjects_v31 RENAME TO ResearchProjects;",
            "CREATE INDEX IF NOT EXISTS IX_ResearchProjects_Work ON ResearchProjects (WorkId, Status, UpdatedUtc);",

            // A research question records who wrote it. Every sibling entity an AI
            // proposal creates - hypotheses, experiments - already carried origin,
            // model, prompt and timestamp; questions were the one kind the model
            // authors that had nowhere to record it, so an exported dossier listed
            // them beside the researcher's own.
            //
            // Rebuilt rather than ALTERed, for replayability. The migration tests fake
            // an older database by dropping the TABLES a later migration creates and
            // rewinding the version stamp - so a table that already existed, like this
            // one, arrives here carrying its current columns. ADD COLUMN then dies on
            // "duplicate column name" and takes ten tests with it. Selecting only the
            // pre-v31 columns makes this statement idempotent whatever shape it meets:
            // the four new ones fall back to their defaults either way.
            @"CREATE TABLE ResearchQuestions_v31 (
                ResearchQuestionId INTEGER PRIMARY KEY,
                ResearchProjectId INTEGER NOT NULL,
                Text TEXT NOT NULL,
                Notes TEXT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                Origin TEXT NOT NULL DEFAULT 'researcher',
                AiModel TEXT NULL,
                AiPrompt TEXT NULL,
                AiGeneratedUtc TEXT NULL,
                CONSTRAINT FK_ResearchQuestions_Projects FOREIGN KEY (ResearchProjectId)
                    REFERENCES ResearchProjects(ResearchProjectId) ON DELETE CASCADE
            );",
            @"INSERT INTO ResearchQuestions_v31
                (ResearchQuestionId, ResearchProjectId, Text, Notes, SortOrder, CreatedUtc, UpdatedUtc)
              SELECT ResearchQuestionId, ResearchProjectId, Text, Notes, SortOrder, CreatedUtc, UpdatedUtc
              FROM ResearchQuestions;",
            "DROP TABLE ResearchQuestions;",
            "ALTER TABLE ResearchQuestions_v31 RENAME TO ResearchQuestions;",
            "CREATE INDEX IF NOT EXISTS IX_ResearchQuestions_Project ON ResearchQuestions (ResearchProjectId, SortOrder);"
        },

        // Out of numerical order below: 33 was written as 32 on a branch, and became
        // 33 when the assessments migration reached main first. ApplyMigrationsAsync
        // walks the version numbers rather than this literal's order, so the sequence
        // is still 32 then 33 - but do not read the file expecting otherwise.

        // Which collection an edition came from, recorded on the edition itself.
        //
        // The search window needs to tell CSEL from the classical Latin texts, and
        // the namespace cannot: both are latinLit, as both Greek collections are
        // greekLit. The download folder can, and was the first answer - but a
        // folder is where the file was, not what the text is. Install somewhere
        // custom, move the data folder, or open the same library on another
        // machine or Windows account, and every path stops matching while the
        // texts sit there unchanged. The database is portable; a path is not.
        //
        // So the collection is stamped onto the edition at ingest and travels with
        // the library. Nothing here reads the XML: the files can be deleted once
        // imported, which was already true and stays true.
        // Rebuilt rather than ALTERed. ADD COLUMN is not replayable - the migration
        // tests fake an older database by rewinding the version stamp while the
        // table keeps its current shape, and the second run dies on "duplicate
        // column name". An index on the new column makes it worse still, since the
        // rewind's DROP COLUMN then fails against the index that depends on it.
        // Selecting only the pre-33 columns makes this idempotent whatever shape it
        // meets.
        [33] = new[]
        {
            @"CREATE TABLE Editions_v33 (
                EditionId   INTEGER PRIMARY KEY,
                WorkId      INTEGER NOT NULL,
                CtsUrn      TEXT NOT NULL,
                Kind        TEXT NOT NULL,
                Language    TEXT NULL,
                Translator  TEXT NULL,
                SourcePath  TEXT NULL,
                Orthography TEXT NULL,
                Collection  TEXT NULL,
                CONSTRAINT UQ_Editions_CtsUrn UNIQUE (CtsUrn),
                CONSTRAINT FK_Editions_Works FOREIGN KEY (WorkId) REFERENCES Works(WorkId)
            );",
            @"INSERT INTO Editions_v33
                (EditionId, WorkId, CtsUrn, Kind, Language, Translator, SourcePath, Orthography)
              SELECT EditionId, WorkId, CtsUrn, Kind, Language, Translator, SourcePath, Orthography
              FROM Editions;",
            "DROP TABLE Editions;",
            "ALTER TABLE Editions_v33 RENAME TO Editions;",
            "CREATE INDEX IF NOT EXISTS IX_Editions_WorkId ON Editions (WorkId);",

            // Best effort for libraries that already exist, from the only signal
            // they carry. A collection installed to a custom folder will not match
            // and stays NULL - it shows as "Other" in the filter until that step is
            // run again, which stamps it properly.
            @"UPDATE Editions SET Collection = 'perseus-greek'
              WHERE Collection IS NULL AND SourcePath LIKE '%\greek-texts\%';",
            @"UPDATE Editions SET Collection = 'perseus-latin'
              WHERE Collection IS NULL AND SourcePath LIKE '%\latin-texts\%';",
            @"UPDATE Editions SET Collection = 'first1k-greek'
              WHERE Collection IS NULL AND SourcePath LIKE '%\first1k-greek\%';",
            @"UPDATE Editions SET Collection = 'csel'
              WHERE Collection IS NULL AND SourcePath LIKE '%\csel\%';",
            @"UPDATE Editions SET Collection = 'renaissance'
              WHERE Collection IS NULL AND SourcePath LIKE '%\english-texts\%';",
            @"UPDATE Editions SET Collection = 'menota'
              WHERE Collection IS NULL AND SourcePath LIKE '%\menota\%';",

            "CREATE INDEX IF NOT EXISTS IX_Editions_Collection ON Editions (Collection);"
        },

        // An assessment belongs to the source it assesses.
        //
        // (SourceKind, SourceId) named a row without referencing it. SQLite reuses
        // rowids, so deleting the highest-numbered evidence item and adding another gave
        // the new one the old number, and an assessment written about the first silently
        // became an assessment of the second - a researcher's judgment reattributed to a
        // passage they had never read. Nothing corrected it short of re-saving that
        // hypothesis's matrix.
        //
        // Four typed columns with real foreign keys and ON DELETE CASCADE: removing a
        // source now removes what was said about it. A CHECK keeps exactly one set.
        //
        // SourceKind stays, and SourceId stays as a generated column over the four. That
        // is what makes this replayable: run against a database already in this shape,
        // the SELECT below still resolves both names and rebuilds the same rows.
        [32] = new[]
        {
            @"CREATE TABLE ResearchHypothesisAssessments_v32 (
                ResearchHypothesisAssessmentId INTEGER PRIMARY KEY,
                ResearchHypothesisId INTEGER NOT NULL,
                SourceKind TEXT NOT NULL,
                EvidenceItemId INTEGER NULL,
                ResearchFindingId INTEGER NULL,
                ScholarlyClaimId INTEGER NULL,
                ResearchEchoResultId INTEGER NULL,
                SourceId INTEGER GENERATED ALWAYS AS (COALESCE(EvidenceItemId, ResearchFindingId,
                    ScholarlyClaimId, ResearchEchoResultId)) VIRTUAL,
                Relationship TEXT NOT NULL DEFAULT 'contextualizes',
                Strength TEXT NOT NULL DEFAULT 'moderate',
                ResearcherNote TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                CONSTRAINT CK_ResearchHypothesisAssessments_OneSource CHECK (
                    (EvidenceItemId IS NOT NULL) + (ResearchFindingId IS NOT NULL)
                  + (ScholarlyClaimId IS NOT NULL) + (ResearchEchoResultId IS NOT NULL) = 1),
                CONSTRAINT FK_ResearchHypothesisAssessments_Hypotheses FOREIGN KEY (ResearchHypothesisId)
                    REFERENCES ResearchHypotheses(ResearchHypothesisId) ON DELETE CASCADE,
                CONSTRAINT FK_ResearchHypothesisAssessments_Evidence FOREIGN KEY (EvidenceItemId)
                    REFERENCES EvidenceItems(EvidenceItemId) ON DELETE CASCADE,
                CONSTRAINT FK_ResearchHypothesisAssessments_Findings FOREIGN KEY (ResearchFindingId)
                    REFERENCES ResearchFindings(ResearchFindingId) ON DELETE CASCADE,
                CONSTRAINT FK_ResearchHypothesisAssessments_Claims FOREIGN KEY (ScholarlyClaimId)
                    REFERENCES ScholarlyClaims(ScholarlyClaimId) ON DELETE CASCADE,
                CONSTRAINT FK_ResearchHypothesisAssessments_EchoResults FOREIGN KEY (ResearchEchoResultId)
                    REFERENCES ResearchEchoResults(ResearchEchoResultId) ON DELETE CASCADE
            );",

            // Rows whose source no longer exists are dropped rather than carried across.
            // They are the ones this migration exists to prevent: already detached, and
            // one rowid reuse away from being silently reattached to something else.
            @"INSERT INTO ResearchHypothesisAssessments_v32
                (ResearchHypothesisAssessmentId, ResearchHypothesisId, SourceKind,
                 EvidenceItemId, ResearchFindingId, ScholarlyClaimId, ResearchEchoResultId,
                 Relationship, Strength, ResearcherNote, CreatedUtc, UpdatedUtc)
              SELECT a.ResearchHypothesisAssessmentId, a.ResearchHypothesisId, a.SourceKind,
                     CASE WHEN a.SourceKind = 'evidence' THEN a.SourceId END,
                     CASE WHEN a.SourceKind = 'finding' THEN a.SourceId END,
                     CASE WHEN a.SourceKind = 'scholarlyclaim' THEN a.SourceId END,
                     CASE WHEN a.SourceKind = 'echoresult' THEN a.SourceId END,
                     a.Relationship, a.Strength, a.ResearcherNote, a.CreatedUtc, a.UpdatedUtc
              FROM ResearchHypothesisAssessments a
              WHERE (a.SourceKind = 'evidence'
                     AND EXISTS (SELECT 1 FROM EvidenceItems t WHERE t.EvidenceItemId = a.SourceId))
                 OR (a.SourceKind = 'finding'
                     AND EXISTS (SELECT 1 FROM ResearchFindings t WHERE t.ResearchFindingId = a.SourceId))
                 OR (a.SourceKind = 'scholarlyclaim'
                     AND EXISTS (SELECT 1 FROM ScholarlyClaims t WHERE t.ScholarlyClaimId = a.SourceId))
                 OR (a.SourceKind = 'echoresult'
                     AND EXISTS (SELECT 1 FROM ResearchEchoResults t WHERE t.ResearchEchoResultId = a.SourceId));",

            "DROP TABLE ResearchHypothesisAssessments;",
            "ALTER TABLE ResearchHypothesisAssessments_v32 RENAME TO ResearchHypothesisAssessments;",
            "CREATE INDEX IF NOT EXISTS IX_ResearchHypothesisAssessments_Hypothesis ON ResearchHypothesisAssessments (ResearchHypothesisId, SourceKind, SourceId);",
            "CREATE UNIQUE INDEX IF NOT EXISTS UX_ResearchHypothesisAssessments_Source ON ResearchHypothesisAssessments (ResearchHypothesisId, SourceKind, SourceId);"
        },

        // A recent search remembers which collections it was narrowed to.
        //
        // It remembered every other filter already, so replaying one silently widened it
        // back to the whole library - the single filter that quietly did not come back,
        // in a list whose entire promise is that it reflects what you actually ran.
        //
        // Stored as the collection keys, comma separated, for the same reason the author
        // is kept by name and the era by label: those outlive a re-ingest, and a row id
        // does not.
        //
        // Rebuilt rather than ALTERed, per the convention above - selecting only the
        // pre-34 columns keeps it idempotent whatever shape it meets.
        [34] = new[]
        {
            @"CREATE TABLE RecentSearches_v34 (
                RecentSearchId INTEGER PRIMARY KEY AUTOINCREMENT,
                Name           TEXT NOT NULL,
                Query          TEXT NOT NULL,
                MatchMode      TEXT NOT NULL,
                Languages      TEXT NOT NULL DEFAULT '',
                Corpora        TEXT NOT NULL DEFAULT '',
                Collections    TEXT NOT NULL DEFAULT '',
                OriginalsOnly  INTEGER NULL,
                AuthorName     TEXT NULL,
                TagName        TEXT NULL,
                BookmarkedOnly INTEGER NOT NULL DEFAULT 0,
                EraLabel       TEXT NULL,
                CreatedAt      TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT UQ_RecentSearches_Name UNIQUE (Name)
            );",
            @"INSERT INTO RecentSearches_v34
                (RecentSearchId, Name, Query, MatchMode, Languages, Corpora, OriginalsOnly,
                 AuthorName, TagName, BookmarkedOnly, EraLabel, CreatedAt)
              SELECT RecentSearchId, Name, Query, MatchMode, Languages, Corpora, OriginalsOnly,
                     AuthorName, TagName, BookmarkedOnly, EraLabel, CreatedAt
              FROM RecentSearches;",
            "DROP TABLE RecentSearches;",
            "ALTER TABLE RecentSearches_v34 RENAME TO RecentSearches;"
        },

        // v35: which lines are verse.
        //
        // TEI distinguishes a verse line from a prose paragraph - <l> against
        // <p> - and the parser has been discarding that since the first
        // ingest. Both are leaves, both became a node of kind 'line', and
        // nothing downstream could tell the Aeneid from the Institutio
        // Oratoria. It is not recoverable from the stored text either: the
        // Greek and Latin as Perseus prints it carries no vowel-length marks,
        // so a line's shape lives in the markup and nowhere else.
        //
        // A column of its own rather than a NodeKind value, because the two
        // are different axes and a node has both. A speaker attribution in a
        // verse play is a Speaker and is not verse; a chorus line is a Line
        // and is. Making verse a kind would have moved every line of poetry
        // out of 'line', which is the exact value the frequency-based
        // features filter to - Homer would have vanished from word counts,
        // core vocabulary and Burrows's Delta, silently and everywhere.
        //
        // Defaulting to 0 leaves an existing library reading exactly as it
        // did, and is the honest value besides: an unlabelled row is not
        // known to be verse, which is what 0 says. Only a re-ingest fills it
        // in, as with NodeKind in migration 14.
        [35] = new[]
        {
            @"ALTER TABLE TextNodes ADD COLUMN IsVerse INTEGER NOT NULL DEFAULT 0;"
        },

        // v36: store the word index once instead of twice.
        //
        // See WordIndexTableDdl for the measurements. The short version is that
        // the old shape kept every (word, line) pair in the table's rowid
        // B-tree AND again in a covering index that was the only thing ever
        // read - 1,054 MB where 474 MB does the same work.
        //
        // Dropped and recreated empty rather than copied across. The word index
        // is pure derived data with nothing of the reader's in it, rebuilt from
        // the corpus by one pass of the Setup Wizard, and copying 26 million
        // rows into a new table would need room for both at once - on a
        // database this size that is the one migration step likeliest to run a
        // disk out of space.
        //
        // The cost is that search falls back to its no-index path until the
        // index is rebuilt, which is slower and loses accent-insensitivity but
        // is never wrong. SetupWizardForm already compares indexed lines
        // against total lines and says when the index needs building, so an
        // upgraded library reports itself as needing exactly what it needs.
        [36] = new[]
        {
            "DROP INDEX IF EXISTS IX_WordIndex_Word;",
            "DROP TABLE IF EXISTS WordIndex;",
            WordIndexTableDdl
        }
    };

    // Nullable WorkId with ON DELETE SET NULL: removing a work from the library
    // detaches the research rather than destroying it or blocking the removal.
    // WorkCtsUrn is how it finds its way back after a re-ingest. Must stay
    // column-for-column identical to migration 31's rebuild - fresh databases only
    // ever run this statement, upgraded ones only ever run that migration.
    private const string ResearchProjectsDdl = @"CREATE TABLE IF NOT EXISTS ResearchProjects (
        ResearchProjectId INTEGER PRIMARY KEY,
        WorkId INTEGER NULL,
        WorkCtsUrn TEXT NULL,
        Name TEXT NOT NULL,
        Status TEXT NOT NULL DEFAULT 'active',
        Notes TEXT NULL,
        CreatedUtc TEXT NOT NULL,
        UpdatedUtc TEXT NOT NULL,
        CONSTRAINT FK_ResearchProjects_Works FOREIGN KEY (WorkId)
            REFERENCES Works(WorkId) ON DELETE SET NULL
    );";

    private const string ResearchQuestionsDdl = @"CREATE TABLE IF NOT EXISTS ResearchQuestions (
        ResearchQuestionId INTEGER PRIMARY KEY,
        ResearchProjectId INTEGER NOT NULL,
        Text TEXT NOT NULL,
        Notes TEXT NULL,
        SortOrder INTEGER NOT NULL DEFAULT 0,
        CreatedUtc TEXT NOT NULL,
        UpdatedUtc TEXT NOT NULL,
        Origin TEXT NOT NULL DEFAULT 'researcher',
        AiModel TEXT NULL,
        AiPrompt TEXT NULL,
        AiGeneratedUtc TEXT NULL,
        CONSTRAINT FK_ResearchQuestions_Projects FOREIGN KEY (ResearchProjectId)
            REFERENCES ResearchProjects(ResearchProjectId) ON DELETE CASCADE
    );";

    private const string EvidenceItemsDdl = @"CREATE TABLE IF NOT EXISTS EvidenceItems (
        EvidenceItemId INTEGER PRIMARY KEY,
        ResearchProjectId INTEGER NOT NULL,
        ResearchQuestionId INTEGER NULL,
        Title TEXT NOT NULL,
        EvidenceType TEXT NOT NULL,
        SourceType TEXT NULL,
        StableIdentifier TEXT NULL,
        CanonicalReference TEXT NULL,
        Provenance TEXT NULL,
        Excerpt TEXT NULL,
        Judgment TEXT NOT NULL DEFAULT 'uncertain',
        Relationship TEXT NOT NULL DEFAULT 'contextualizes',
        ResearcherNote TEXT NULL,
        SortOrder INTEGER NOT NULL DEFAULT 0,
        CreatedUtc TEXT NOT NULL,
        UpdatedUtc TEXT NOT NULL,
        CONSTRAINT FK_EvidenceItems_Projects FOREIGN KEY (ResearchProjectId)
            REFERENCES ResearchProjects(ResearchProjectId) ON DELETE CASCADE,
        CONSTRAINT FK_EvidenceItems_Questions FOREIGN KEY (ResearchQuestionId)
            REFERENCES ResearchQuestions(ResearchQuestionId) ON DELETE SET NULL
    );";

    private const string EvidenceGenerationMetadataDdl = @"CREATE TABLE IF NOT EXISTS EvidenceGenerationMetadata (
        EvidenceItemId INTEGER PRIMARY KEY,
        Origin TEXT NOT NULL DEFAULT 'manual',
        Interpretation TEXT NULL,
        InterpretationAuthor TEXT NULL,
        GeneratorPrompt TEXT NULL,
        GeneratedUtc TEXT NULL,
        CONSTRAINT FK_EvidenceGenerationMetadata_Evidence FOREIGN KEY (EvidenceItemId)
            REFERENCES EvidenceItems(EvidenceItemId) ON DELETE CASCADE
    );";

    private const string ResearchLogEntriesDdl = @"CREATE TABLE IF NOT EXISTS ResearchLogEntries (
        ResearchLogEntryId INTEGER PRIMARY KEY,
        ResearchProjectId INTEGER NOT NULL,
        Kind TEXT NOT NULL,
        Summary TEXT NOT NULL,
        Details TEXT NULL,
        ResearchQuestionId INTEGER NULL,
        EvidenceItemId INTEGER NULL,
        CreatedUtc TEXT NOT NULL,
        CONSTRAINT FK_ResearchLogEntries_Projects FOREIGN KEY (ResearchProjectId)
            REFERENCES ResearchProjects(ResearchProjectId) ON DELETE CASCADE,
        CONSTRAINT FK_ResearchLogEntries_Questions FOREIGN KEY (ResearchQuestionId)
            REFERENCES ResearchQuestions(ResearchQuestionId) ON DELETE SET NULL,
        CONSTRAINT FK_ResearchLogEntries_Evidence FOREIGN KEY (EvidenceItemId)
            REFERENCES EvidenceItems(EvidenceItemId) ON DELETE SET NULL
    );";

    private const string ScholarlyClaimsDdl = @"CREATE TABLE IF NOT EXISTS ScholarlyClaims (
        ScholarlyClaimId INTEGER PRIMARY KEY,
        ResearchProjectId INTEGER NOT NULL,
        ResearchQuestionId INTEGER NULL,
        SourceEvidenceItemId INTEGER NULL,
        Claimant TEXT NOT NULL,
        ClaimText TEXT NOT NULL,
        Locator TEXT NULL,
        Relationship TEXT NOT NULL DEFAULT 'contextualizes',
        Judgment TEXT NOT NULL DEFAULT 'uncertain',
        Notes TEXT NULL,
        SortOrder INTEGER NOT NULL DEFAULT 0,
        CreatedUtc TEXT NOT NULL,
        UpdatedUtc TEXT NOT NULL,
        CONSTRAINT FK_ScholarlyClaims_Projects FOREIGN KEY (ResearchProjectId)
            REFERENCES ResearchProjects(ResearchProjectId) ON DELETE CASCADE,
        CONSTRAINT FK_ScholarlyClaims_Questions FOREIGN KEY (ResearchQuestionId)
            REFERENCES ResearchQuestions(ResearchQuestionId) ON DELETE SET NULL,
        CONSTRAINT FK_ScholarlyClaims_Evidence FOREIGN KEY (SourceEvidenceItemId)
            REFERENCES EvidenceItems(EvidenceItemId) ON DELETE SET NULL
    );";

    private const string EvidenceAttachmentsDdl = @"CREATE TABLE IF NOT EXISTS EvidenceAttachments (
        EvidenceAttachmentId INTEGER PRIMARY KEY,
        EvidenceItemId INTEGER NOT NULL,
        FilePath TEXT NOT NULL,
        FileName TEXT NOT NULL,
        MediaType TEXT NOT NULL DEFAULT 'application/pdf',
        Sha256 TEXT NOT NULL,
        FileSize INTEGER NOT NULL,
        FileModifiedUtc TEXT NOT NULL,
        CreatedUtc TEXT NOT NULL,
        UpdatedUtc TEXT NOT NULL,
        CONSTRAINT FK_EvidenceAttachments_Evidence FOREIGN KEY (EvidenceItemId)
            REFERENCES EvidenceItems(EvidenceItemId) ON DELETE CASCADE
    );";

    private const string EvidencePageAnnotationsDdl = @"CREATE TABLE IF NOT EXISTS EvidencePageAnnotations (
        EvidencePageAnnotationId INTEGER PRIMARY KEY,
        EvidenceAttachmentId INTEGER NOT NULL,
        PageNumber INTEGER NOT NULL CHECK (PageNumber > 0),
        QuotedText TEXT NULL,
        Note TEXT NULL,
        Judgment TEXT NOT NULL DEFAULT 'uncertain',
        CreatedUtc TEXT NOT NULL,
        UpdatedUtc TEXT NOT NULL,
        CONSTRAINT FK_EvidencePageAnnotations_Attachments FOREIGN KEY (EvidenceAttachmentId)
            REFERENCES EvidenceAttachments(EvidenceAttachmentId) ON DELETE CASCADE
    );";

    private const string EvidenceBibliographyMetadataDdl = @"CREATE TABLE IF NOT EXISTS EvidenceBibliographyMetadata (
        EvidenceItemId INTEGER PRIMARY KEY,
        ImportFormat TEXT NOT NULL DEFAULT 'Manual',
        EntryType TEXT NOT NULL DEFAULT 'MISC',
        CiteKey TEXT NULL,
        Title TEXT NOT NULL,
        AuthorsJson TEXT NOT NULL DEFAULT '[]',
        Year TEXT NULL,
        ContainerTitle TEXT NULL,
        Volume TEXT NULL,
        Issue TEXT NULL,
        Pages TEXT NULL,
        Publisher TEXT NULL,
        Doi TEXT NULL,
        Url TEXT NULL,
        Isbn TEXT NULL,
        Abstract TEXT NULL,
        KeywordsJson TEXT NOT NULL DEFAULT '[]',
        CreatedUtc TEXT NOT NULL,
        UpdatedUtc TEXT NOT NULL,
        CONSTRAINT FK_EvidenceBibliographyMetadata_Evidence FOREIGN KEY (EvidenceItemId)
            REFERENCES EvidenceItems(EvidenceItemId) ON DELETE CASCADE
    );";

    private const string ResearchCorpusSnapshotsDdl = @"CREATE TABLE IF NOT EXISTS ResearchCorpusSnapshots (
        ResearchCorpusSnapshotId INTEGER PRIMARY KEY,
        ResearchProjectId INTEGER NOT NULL,
        Name TEXT NOT NULL,
        Scope TEXT NOT NULL,
        AppVersion TEXT NOT NULL,
        Notes TEXT NULL,
        WorkCount INTEGER NOT NULL,
        EditionCount INTEGER NOT NULL,
        TextNodeCount INTEGER NOT NULL,
        CreatedUtc TEXT NOT NULL,
        CONSTRAINT FK_ResearchCorpusSnapshots_Projects FOREIGN KEY (ResearchProjectId)
            REFERENCES ResearchProjects(ResearchProjectId) ON DELETE CASCADE
    );";

    private const string ResearchCorpusSnapshotEntriesDdl = @"CREATE TABLE IF NOT EXISTS ResearchCorpusSnapshotEntries (
        ResearchCorpusSnapshotEntryId INTEGER PRIMARY KEY,
        ResearchCorpusSnapshotId INTEGER NOT NULL,
        AuthorCtsUrn TEXT NOT NULL,
        AuthorName TEXT NOT NULL,
        WorkCtsUrn TEXT NOT NULL,
        WorkTitle TEXT NOT NULL,
        CitationScheme TEXT NULL,
        AttributionStatus TEXT NOT NULL,
        AttributionNote TEXT NULL,
        AttributionSetByUser INTEGER NOT NULL,
        EditionCtsUrn TEXT NULL,
        EditionKind TEXT NULL,
        Language TEXT NULL,
        Translator TEXT NULL,
        SourcePath TEXT NULL,
        Orthography TEXT NULL,
        TextNodeCount INTEGER NOT NULL,
        ContentSha256 TEXT NULL,
        CONSTRAINT FK_ResearchCorpusSnapshotEntries_Snapshots FOREIGN KEY (ResearchCorpusSnapshotId)
            REFERENCES ResearchCorpusSnapshots(ResearchCorpusSnapshotId) ON DELETE CASCADE
    );";

    private const string ResearchReadingItemsDdl = @"CREATE TABLE IF NOT EXISTS ResearchReadingItems (
        ResearchReadingItemId INTEGER PRIMARY KEY,
        ResearchProjectId INTEGER NOT NULL,
        ResearchQuestionId INTEGER NULL,
        Kind TEXT NOT NULL,
        Status TEXT NOT NULL DEFAULT 'queued',
        Priority TEXT NOT NULL DEFAULT 'normal',
        Title TEXT NOT NULL,
        Purpose TEXT NULL,
        WorkCtsUrn TEXT NULL,
        EditionCtsUrn TEXT NULL,
        CitationRef TEXT NULL,
        LinkedEvidenceItemId INTEGER NULL,
        StableIdentifier TEXT NULL,
        Locator TEXT NULL,
        Quotation TEXT NULL,
        Notes TEXT NULL,
        PromotedEvidenceItemId INTEGER NULL,
        SortOrder INTEGER NOT NULL DEFAULT 0,
        CreatedUtc TEXT NOT NULL,
        UpdatedUtc TEXT NOT NULL,
        CONSTRAINT FK_ResearchReadingItems_Projects FOREIGN KEY (ResearchProjectId)
            REFERENCES ResearchProjects(ResearchProjectId) ON DELETE CASCADE,
        CONSTRAINT FK_ResearchReadingItems_Questions FOREIGN KEY (ResearchQuestionId)
            REFERENCES ResearchQuestions(ResearchQuestionId) ON DELETE SET NULL,
        CONSTRAINT FK_ResearchReadingItems_LinkedEvidence FOREIGN KEY (LinkedEvidenceItemId)
            REFERENCES EvidenceItems(EvidenceItemId) ON DELETE SET NULL,
        CONSTRAINT FK_ResearchReadingItems_PromotedEvidence FOREIGN KEY (PromotedEvidenceItemId)
            REFERENCES EvidenceItems(EvidenceItemId) ON DELETE SET NULL
    );";

    private const string ResearchFindingsDdl = @"CREATE TABLE IF NOT EXISTS ResearchFindings (
        ResearchFindingId INTEGER PRIMARY KEY,
        ResearchProjectId INTEGER NOT NULL,
        ResearchQuestionId INTEGER NULL,
        Title TEXT NOT NULL,
        Statement TEXT NOT NULL,
        Status TEXT NOT NULL DEFAULT 'hypothesis',
        ResearcherConclusion TEXT NULL,
        AiCandidateSynthesis TEXT NULL,
        AiModel TEXT NULL,
        AiPrompt TEXT NULL,
        AiGeneratedUtc TEXT NULL,
        SortOrder INTEGER NOT NULL DEFAULT 0,
        CreatedUtc TEXT NOT NULL,
        UpdatedUtc TEXT NOT NULL,
        CONSTRAINT FK_ResearchFindings_Projects FOREIGN KEY (ResearchProjectId)
            REFERENCES ResearchProjects(ResearchProjectId) ON DELETE CASCADE,
        CONSTRAINT FK_ResearchFindings_Questions FOREIGN KEY (ResearchQuestionId)
            REFERENCES ResearchQuestions(ResearchQuestionId) ON DELETE SET NULL
    );";

    private const string ResearchFindingEvidenceDdl = @"CREATE TABLE IF NOT EXISTS ResearchFindingEvidence (
        ResearchFindingId INTEGER NOT NULL,
        EvidenceItemId INTEGER NOT NULL,
        Relationship TEXT NOT NULL DEFAULT 'contextualizes',
        Note TEXT NULL,
        PRIMARY KEY (ResearchFindingId, EvidenceItemId),
        CONSTRAINT FK_ResearchFindingEvidence_Findings FOREIGN KEY (ResearchFindingId)
            REFERENCES ResearchFindings(ResearchFindingId) ON DELETE CASCADE,
        CONSTRAINT FK_ResearchFindingEvidence_Evidence FOREIGN KEY (EvidenceItemId)
            REFERENCES EvidenceItems(EvidenceItemId) ON DELETE CASCADE
    );";

    private const string ResearchEchoInvestigationsDdl = @"CREATE TABLE IF NOT EXISTS ResearchEchoInvestigations (
        ResearchEchoInvestigationId INTEGER PRIMARY KEY,
        ResearchProjectId INTEGER NOT NULL,
        ResearchQuestionId INTEGER NULL,
        ResearchFindingId INTEGER NULL,
        Method TEXT NOT NULL,
        Title TEXT NOT NULL,
        SourceWorkId INTEGER NOT NULL,
        SourceTextNodeId INTEGER NOT NULL,
        SourceWorkCtsUrn TEXT NOT NULL,
        SourceEditionCtsUrn TEXT NOT NULL,
        SourceCitationRef TEXT NOT NULL,
        SourceText TEXT NOT NULL,
        SourceLanguage TEXT NULL,
        TargetScope TEXT NULL,
        Settings TEXT NULL,
        AiModel TEXT NULL,
        AiPrompt TEXT NULL,
        AiGeneratedUtc TEXT NULL,
        CreatedUtc TEXT NOT NULL,
        UpdatedUtc TEXT NOT NULL,
        CONSTRAINT FK_ResearchEchoInvestigations_Projects FOREIGN KEY (ResearchProjectId)
            REFERENCES ResearchProjects(ResearchProjectId) ON DELETE CASCADE,
        CONSTRAINT FK_ResearchEchoInvestigations_Questions FOREIGN KEY (ResearchQuestionId)
            REFERENCES ResearchQuestions(ResearchQuestionId) ON DELETE SET NULL,
        CONSTRAINT FK_ResearchEchoInvestigations_Findings FOREIGN KEY (ResearchFindingId)
            REFERENCES ResearchFindings(ResearchFindingId) ON DELETE SET NULL
    );";

    private const string ResearchEchoResultsDdl = @"CREATE TABLE IF NOT EXISTS ResearchEchoResults (
        ResearchEchoResultId INTEGER PRIMARY KEY,
        ResearchEchoInvestigationId INTEGER NOT NULL,
        TargetWorkId INTEGER NOT NULL,
        TargetTextNodeId INTEGER NOT NULL,
        TargetAuthorName TEXT NOT NULL,
        TargetWorkTitle TEXT NOT NULL,
        TargetWorkCtsUrn TEXT NOT NULL,
        TargetEditionCtsUrn TEXT NOT NULL,
        TargetCitationRef TEXT NOT NULL,
        TargetText TEXT NOT NULL,
        TargetLanguage TEXT NULL,
        Score REAL NULL,
        ScoreLabel TEXT NULL,
        Rationale TEXT NULL,
        Disposition TEXT NOT NULL DEFAULT 'pending',
        ResearcherNote TEXT NULL,
        ConnectionType TEXT NOT NULL DEFAULT 'unclassified',
        Directionality TEXT NOT NULL DEFAULT 'unknown',
        MotifTags TEXT NULL,
        ParallelNote TEXT NULL,
        EvidenceItemId INTEGER NULL,
        SortOrder INTEGER NOT NULL DEFAULT 0,
        CreatedUtc TEXT NOT NULL,
        UpdatedUtc TEXT NOT NULL,
        CONSTRAINT FK_ResearchEchoResults_Investigations FOREIGN KEY (ResearchEchoInvestigationId)
            REFERENCES ResearchEchoInvestigations(ResearchEchoInvestigationId) ON DELETE CASCADE,
        CONSTRAINT FK_ResearchEchoResults_Evidence FOREIGN KEY (EvidenceItemId)
            REFERENCES EvidenceItems(EvidenceItemId) ON DELETE SET NULL
    );";

    private const string ResearchEchoParallelAnalysesDdl = @"CREATE TABLE IF NOT EXISTS ResearchEchoParallelAnalyses (
        ResearchEchoParallelAnalysisId INTEGER PRIMARY KEY,
        ResearchEchoResultId INTEGER NOT NULL,
        Model TEXT NOT NULL,
        Prompt TEXT NOT NULL,
        Summary TEXT NOT NULL,
        SharedFeatures TEXT NULL,
        ImportantDifferences TEXT NULL,
        LexicalObservations TEXT NULL,
        AlternativeExplanations TEXT NULL,
        VerificationTasks TEXT NULL,
        SuggestedMotifs TEXT NULL,
        SuggestedConnectionType TEXT NOT NULL DEFAULT 'unclassified',
        SuggestedDirectionality TEXT NOT NULL DEFAULT 'unknown',
        CreatedUtc TEXT NOT NULL,
        CONSTRAINT FK_ResearchEchoParallelAnalyses_Results FOREIGN KEY (ResearchEchoResultId)
            REFERENCES ResearchEchoResults(ResearchEchoResultId) ON DELETE CASCADE
    );";

    private const string ResearchHypothesesDdl = @"CREATE TABLE IF NOT EXISTS ResearchHypotheses (
        ResearchHypothesisId INTEGER PRIMARY KEY,
        ResearchProjectId INTEGER NOT NULL,
        Title TEXT NOT NULL,
        Statement TEXT NOT NULL,
        Status TEXT NOT NULL DEFAULT 'active',
        Origin TEXT NOT NULL DEFAULT 'manual',
        ResearcherNote TEXT NULL,
        AiModel TEXT NULL,
        AiPrompt TEXT NULL,
        AiGeneratedUtc TEXT NULL,
        SortOrder INTEGER NOT NULL DEFAULT 0,
        CreatedUtc TEXT NOT NULL,
        UpdatedUtc TEXT NOT NULL,
        CONSTRAINT FK_ResearchHypotheses_Projects FOREIGN KEY (ResearchProjectId)
            REFERENCES ResearchProjects(ResearchProjectId) ON DELETE CASCADE
    );";

    /// <summary>
    /// One typed, enforced link per source kind, rather than a loose (SourceKind,
    /// SourceId) pair pointing at whatever now holds that rowid.
    ///
    /// SQLite reuses rowids: delete the highest-numbered evidence item and the next one
    /// inserted takes its number back. An assessment recorded against the old row then
    /// described a passage the researcher had never assessed, silently, and only a
    /// re-save of that hypothesis would have corrected it. The typed columns carry real
    /// foreign keys, so deleting a source now takes its assessments with it.
    ///
    /// SourceKind stays as the discriminator the reads order by, and SourceId stays as a
    /// generated column over the four - which is also what lets migration 32 be replayed
    /// against a database already in this shape, since both names still resolve.
    /// </summary>
    private const string ResearchHypothesisAssessmentsDdl = @"CREATE TABLE IF NOT EXISTS ResearchHypothesisAssessments (
        ResearchHypothesisAssessmentId INTEGER PRIMARY KEY,
        ResearchHypothesisId INTEGER NOT NULL,
        SourceKind TEXT NOT NULL,
        EvidenceItemId INTEGER NULL,
        ResearchFindingId INTEGER NULL,
        ScholarlyClaimId INTEGER NULL,
        ResearchEchoResultId INTEGER NULL,
        SourceId INTEGER GENERATED ALWAYS AS (COALESCE(EvidenceItemId, ResearchFindingId,
            ScholarlyClaimId, ResearchEchoResultId)) VIRTUAL,
        Relationship TEXT NOT NULL DEFAULT 'contextualizes',
        Strength TEXT NOT NULL DEFAULT 'moderate',
        ResearcherNote TEXT NULL,
        CreatedUtc TEXT NOT NULL,
        UpdatedUtc TEXT NOT NULL,
        CONSTRAINT CK_ResearchHypothesisAssessments_OneSource CHECK (
            (EvidenceItemId IS NOT NULL) + (ResearchFindingId IS NOT NULL)
          + (ScholarlyClaimId IS NOT NULL) + (ResearchEchoResultId IS NOT NULL) = 1),
        CONSTRAINT FK_ResearchHypothesisAssessments_Hypotheses FOREIGN KEY (ResearchHypothesisId)
            REFERENCES ResearchHypotheses(ResearchHypothesisId) ON DELETE CASCADE,
        CONSTRAINT FK_ResearchHypothesisAssessments_Evidence FOREIGN KEY (EvidenceItemId)
            REFERENCES EvidenceItems(EvidenceItemId) ON DELETE CASCADE,
        CONSTRAINT FK_ResearchHypothesisAssessments_Findings FOREIGN KEY (ResearchFindingId)
            REFERENCES ResearchFindings(ResearchFindingId) ON DELETE CASCADE,
        CONSTRAINT FK_ResearchHypothesisAssessments_Claims FOREIGN KEY (ScholarlyClaimId)
            REFERENCES ScholarlyClaims(ScholarlyClaimId) ON DELETE CASCADE,
        CONSTRAINT FK_ResearchHypothesisAssessments_EchoResults FOREIGN KEY (ResearchEchoResultId)
            REFERENCES ResearchEchoResults(ResearchEchoResultId) ON DELETE CASCADE
    );";

    private const string ResearchExperimentsDdl = @"CREATE TABLE IF NOT EXISTS ResearchExperiments (
        ResearchExperimentId INTEGER PRIMARY KEY,
        ResearchProjectId INTEGER NOT NULL,
        ResearchHypothesisId INTEGER NULL,
        Title TEXT NOT NULL,
        Method TEXT NOT NULL DEFAULT 'manual',
        Status TEXT NOT NULL DEFAULT 'planned',
        PredictedOutcome TEXT NULL,
        FalsificationCriterion TEXT NULL,
        ResearcherNote TEXT NULL,
        Origin TEXT NOT NULL DEFAULT 'manual',
        AiModel TEXT NULL,
        AiPrompt TEXT NULL,
        AiGeneratedUtc TEXT NULL,
        SortOrder INTEGER NOT NULL DEFAULT 0,
        CreatedUtc TEXT NOT NULL,
        UpdatedUtc TEXT NOT NULL,
        CONSTRAINT FK_ResearchExperiments_Projects FOREIGN KEY (ResearchProjectId)
            REFERENCES ResearchProjects(ResearchProjectId) ON DELETE CASCADE,
        CONSTRAINT FK_ResearchExperiments_Hypotheses FOREIGN KEY (ResearchHypothesisId)
            REFERENCES ResearchHypotheses(ResearchHypothesisId) ON DELETE SET NULL
    );";

    private const string PassageInquiriesDdl = @"CREATE TABLE IF NOT EXISTS PassageInquiries (
        PassageInquiryId INTEGER PRIMARY KEY,
        WorkCtsUrn TEXT NOT NULL,
        EditionCtsUrn TEXT NOT NULL,
        CitationRef TEXT NOT NULL,
        AuthorName TEXT NOT NULL,
        WorkTitle TEXT NOT NULL,
        Excerpt TEXT NOT NULL,
        AttentionNote TEXT NOT NULL,
        DraftQuestion TEXT NOT NULL,
        Direction TEXT NOT NULL DEFAULT 'none',
        ResearchProjectId INTEGER NULL,
        CreatedUtc TEXT NOT NULL,
        UpdatedUtc TEXT NOT NULL,
        CONSTRAINT FK_PassageInquiries_Projects FOREIGN KEY (ResearchProjectId)
            REFERENCES ResearchProjects(ResearchProjectId) ON DELETE SET NULL
    );";

    private static readonly string[] SchemaStatements =
    {
        ResearchProjectsDdl,
        ResearchQuestionsDdl,
        EvidenceItemsDdl,
        EvidenceGenerationMetadataDdl,
        ResearchLogEntriesDdl,
        ScholarlyClaimsDdl,
        EvidenceAttachmentsDdl,
        EvidencePageAnnotationsDdl,
        EvidenceBibliographyMetadataDdl,
        ResearchCorpusSnapshotsDdl,
        ResearchCorpusSnapshotEntriesDdl,
        ResearchReadingItemsDdl,
        ResearchFindingsDdl,
        ResearchFindingEvidenceDdl,
        ResearchEchoInvestigationsDdl,
        ResearchEchoResultsDdl,
        ResearchEchoParallelAnalysesDdl,
        ResearchHypothesesDdl,
        ResearchHypothesisAssessmentsDdl,
        ResearchExperimentsDdl,
        PassageInquiriesDdl,
        "CREATE INDEX IF NOT EXISTS IX_ResearchProjects_Work ON ResearchProjects (WorkId, Status, UpdatedUtc);",
        "CREATE INDEX IF NOT EXISTS IX_ResearchQuestions_Project ON ResearchQuestions (ResearchProjectId, SortOrder);",
        "CREATE INDEX IF NOT EXISTS IX_EvidenceItems_Project ON EvidenceItems (ResearchProjectId, SortOrder);",
        "CREATE INDEX IF NOT EXISTS IX_EvidenceItems_Question ON EvidenceItems (ResearchQuestionId);",
        "CREATE INDEX IF NOT EXISTS IX_EvidenceGenerationMetadata_Origin ON EvidenceGenerationMetadata (Origin);",
        "CREATE INDEX IF NOT EXISTS IX_ResearchLogEntries_Project ON ResearchLogEntries (ResearchProjectId, CreatedUtc, ResearchLogEntryId);",
        "CREATE INDEX IF NOT EXISTS IX_ScholarlyClaims_Project ON ScholarlyClaims (ResearchProjectId, SortOrder, ScholarlyClaimId);",
        "CREATE INDEX IF NOT EXISTS IX_ScholarlyClaims_Question ON ScholarlyClaims (ResearchQuestionId);",
        "CREATE INDEX IF NOT EXISTS IX_ScholarlyClaims_Source ON ScholarlyClaims (SourceEvidenceItemId);",
        "CREATE UNIQUE INDEX IF NOT EXISTS UX_EvidenceAttachments_Path ON EvidenceAttachments (EvidenceItemId, FilePath);",
        "CREATE INDEX IF NOT EXISTS IX_EvidencePageAnnotations_Attachment ON EvidencePageAnnotations (EvidenceAttachmentId, PageNumber, EvidencePageAnnotationId);",
        "CREATE INDEX IF NOT EXISTS IX_ResearchCorpusSnapshots_Project ON ResearchCorpusSnapshots (ResearchProjectId, CreatedUtc);",
        "CREATE INDEX IF NOT EXISTS IX_ResearchCorpusSnapshotEntries_Snapshot ON ResearchCorpusSnapshotEntries (ResearchCorpusSnapshotId, WorkCtsUrn, EditionCtsUrn);",
        "CREATE INDEX IF NOT EXISTS IX_ResearchReadingItems_Project ON ResearchReadingItems (ResearchProjectId, Status, Priority, SortOrder, ResearchReadingItemId);",
        "CREATE INDEX IF NOT EXISTS IX_ResearchReadingItems_Question ON ResearchReadingItems (ResearchQuestionId);",
        "CREATE INDEX IF NOT EXISTS IX_ResearchFindings_Project ON ResearchFindings (ResearchProjectId, SortOrder, ResearchFindingId);",
        "CREATE INDEX IF NOT EXISTS IX_ResearchFindings_Question ON ResearchFindings (ResearchQuestionId);",
        "CREATE INDEX IF NOT EXISTS IX_ResearchFindingEvidence_Evidence ON ResearchFindingEvidence (EvidenceItemId);",
        "CREATE INDEX IF NOT EXISTS IX_ResearchEchoInvestigations_Project ON ResearchEchoInvestigations (ResearchProjectId, CreatedUtc, ResearchEchoInvestigationId);",
        "CREATE INDEX IF NOT EXISTS IX_ResearchEchoResults_Investigation ON ResearchEchoResults (ResearchEchoInvestigationId, Disposition, SortOrder, ResearchEchoResultId);",
        "CREATE INDEX IF NOT EXISTS IX_ResearchEchoResults_Evidence ON ResearchEchoResults (EvidenceItemId);",
        "CREATE INDEX IF NOT EXISTS IX_ResearchEchoParallelAnalyses_Result ON ResearchEchoParallelAnalyses (ResearchEchoResultId, CreatedUtc, ResearchEchoParallelAnalysisId);",
        "CREATE INDEX IF NOT EXISTS IX_ResearchHypotheses_Project ON ResearchHypotheses (ResearchProjectId, SortOrder, ResearchHypothesisId);",
        "CREATE INDEX IF NOT EXISTS IX_ResearchHypothesisAssessments_Hypothesis ON ResearchHypothesisAssessments (ResearchHypothesisId, SourceKind, SourceId);",
        // Replaces the UNIQUE table constraint the pre-32 shape carried. It has to be an
        // index now, because SourceId became a generated column.
        "CREATE UNIQUE INDEX IF NOT EXISTS UX_ResearchHypothesisAssessments_Source ON ResearchHypothesisAssessments (ResearchHypothesisId, SourceKind, SourceId);",
        "CREATE INDEX IF NOT EXISTS IX_ResearchExperiments_Project ON ResearchExperiments (ResearchProjectId, Status, SortOrder, ResearchExperimentId);",
        "CREATE UNIQUE INDEX IF NOT EXISTS UX_PassageInquiries_Passage ON PassageInquiries (EditionCtsUrn, CitationRef);",
        "CREATE INDEX IF NOT EXISTS IX_PassageInquiries_Project ON PassageInquiries (ResearchProjectId);",

        // The statements below are also created by migrations, and have to
        // be here as well because a NEW database never runs a migration - it
        // is treated as already current and takes its shape from this list
        // alone. A table that exists only in Migrations therefore exists on
        // every upgraded library and on no fresh one.
        //
        // SavedSearches had exactly that gap: present since v4 for anyone
        // who upgraded, absent for anyone starting from an empty file.
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
            );",

        @"CREATE TABLE IF NOT EXISTS StylometryExperiments (
                ExperimentId    INTEGER PRIMARY KEY,
                CreatedUtc      TEXT    NOT NULL,
                Kind            TEXT    NOT NULL,
                TargetAuthor    TEXT    NOT NULL,
                Language        TEXT    NULL,
                PoolSummary     TEXT    NOT NULL,
                PoolWorkIds     TEXT    NOT NULL,
                Seed            INTEGER NOT NULL,
                Iterations      INTEGER NOT NULL,
                ChunkSize       INTEGER NOT NULL,
                FeatureWordCount INTEGER NOT NULL,
                FoldAccents     INTEGER NOT NULL,
                AlgorithmVersion INTEGER NOT NULL,
                Parameters      TEXT    NOT NULL,
                Metrics         TEXT    NOT NULL,
                Label           TEXT    NULL,
                Notes           TEXT    NULL
            );",

        @"CREATE INDEX IF NOT EXISTS IX_StylometryExperiments_Kind
                ON StylometryExperiments (Kind, TargetAuthor, CreatedUtc);",

        @"CREATE TABLE IF NOT EXISTS StylometryExperimentRows (
                ExperimentId    INTEGER NOT NULL,
                RowIndex        INTEGER NOT NULL,
                WorkId          INTEGER NULL,
                WorkTitle       TEXT    NOT NULL,
                Donor           TEXT    NOT NULL,
                Level           REAL    NOT NULL,
                MeanMargin      REAL    NOT NULL,
                StdDev          REAL    NOT NULL,
                BaselineMargin  REAL    NOT NULL,
                Recovered       INTEGER NOT NULL,
                Trials          INTEGER NOT NULL,
                NearestAuthor   TEXT    NULL,
                TokenCount      INTEGER NOT NULL,
                NearestCount    INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (ExperimentId, RowIndex),
                FOREIGN KEY (ExperimentId) REFERENCES StylometryExperiments(ExperimentId) ON DELETE CASCADE
            );",

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
            AttributionStatus TEXT NOT NULL DEFAULT 'accepted',
            AttributionNote TEXT NULL,
            AttributionSetByUser INTEGER NOT NULL DEFAULT 0,
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
            Orthography TEXT NULL,   -- normalised / diplomatic / NULL; see migration 13
            Collection  TEXT NULL,   -- which downloaded collection this came from; see migration 32
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
        @"CREATE INDEX IF NOT EXISTS IX_Editions_Collection ON Editions (Collection);",
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
            IsAthetized INTEGER NOT NULL DEFAULT 0,
            NodeKind    TEXT NOT NULL DEFAULT 'line',
            IsVerse     INTEGER NOT NULL DEFAULT 0,
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
                Collections    TEXT NOT NULL DEFAULT '',
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

        // Favourite works - a shortlist of the texts you actually return to,
        // out of a corpus of several thousand. Keyed on the work's CTS URN
        // rather than its WorkId, because ids renumber on a re-ingest and a
        // favourites list that quietly repoints at other works would be
        // worse than one that was lost. Mirrored in migration 7; a new
        // database gets this DDL and never runs migrations, so the two have
        // to agree.
        @"CREATE TABLE IF NOT EXISTS FavoriteWorks (
            CtsUrn    TEXT NOT NULL PRIMARY KEY,
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );",

        // Saved stylometric runs. Mirrored in migration 8 - same reason as
        // FavoriteWorks above: a new database gets this DDL and never runs
        // migrations, so the two definitions have to stay in step.
        //
        // Author and title are denormalised into the results on purpose. See
        // the migration 8 comment for why a saved run must not be joined live
        // against Works.
        @"CREATE TABLE IF NOT EXISTS ApparatusEntries (
            ApparatusId INTEGER PRIMARY KEY AUTOINCREMENT,
            EditionId   INTEGER NOT NULL,
            CitationRef TEXT    NOT NULL,
            SortOrder   INTEGER NOT NULL,
            Kind        TEXT    NOT NULL,
            Lemma       TEXT    NULL,
            Witness     TEXT    NULL,
            Content     TEXT    NOT NULL,
            CONSTRAINT FK_ApparatusEntries_Editions
                FOREIGN KEY (EditionId) REFERENCES Editions(EditionId)
        );",

        @"CREATE INDEX IF NOT EXISTS IX_ApparatusEntries_Line
            ON ApparatusEntries (EditionId, CitationRef, SortOrder);",

        @"CREATE TABLE IF NOT EXISTS StylometryRuns (
            RunId             INTEGER PRIMARY KEY AUTOINCREMENT,
            CreatedUtc        TEXT    NOT NULL,
            TargetWorkId      INTEGER NOT NULL,
            TargetEditionId   INTEGER NOT NULL,
            TargetAuthorName  TEXT    NOT NULL,
            TargetWorkTitle   TEXT    NOT NULL,
            Language          TEXT    NOT NULL,
            FeatureWordCount  INTEGER NOT NULL,
            FoldAccents       INTEGER NOT NULL,
            StripElisionMarks INTEGER NOT NULL,
            PoolSize          INTEGER NOT NULL,
            AlgorithmVersion  INTEGER NOT NULL,
            Label             TEXT    NULL,
            Notes             TEXT    NULL,
            TargetTokenCount  INTEGER NULL,
            ChunkSize         INTEGER NOT NULL DEFAULT 0
        );",

        @"CREATE INDEX IF NOT EXISTS IX_StylometryRuns_Target
            ON StylometryRuns (TargetWorkId, AlgorithmVersion, FeatureWordCount, FoldAccents);",

        @"CREATE INDEX IF NOT EXISTS IX_StylometryRuns_Settings
            ON StylometryRuns (Language, AlgorithmVersion, FeatureWordCount, FoldAccents, StripElisionMarks);",

        @"CREATE TABLE IF NOT EXISTS StylometryRunResults (
            RunId       INTEGER NOT NULL,
            Rank        INTEGER NOT NULL,
            WorkId      INTEGER NOT NULL,
            AuthorName  TEXT    NOT NULL,
            WorkTitle   TEXT    NOT NULL,
            Delta       REAL    NOT NULL,
            CONSTRAINT PK_StylometryRunResults PRIMARY KEY (RunId, Rank),
            CONSTRAINT FK_StylometryRunResults_Runs
                FOREIGN KEY (RunId) REFERENCES StylometryRuns(RunId) ON DELETE CASCADE
        );",

        @"CREATE INDEX IF NOT EXISTS IX_StylometryRunResults_Author
            ON StylometryRunResults (RunId, AuthorName, Rank);",

        @"CREATE TABLE IF NOT EXISTS StylometryRunFeatures (
            RunId             INTEGER NOT NULL,
            Rank              INTEGER NOT NULL,
            Word              TEXT    NOT NULL,
            RelativeFrequency REAL    NOT NULL,
            CONSTRAINT PK_StylometryRunFeatures PRIMARY KEY (RunId, Rank),
            CONSTRAINT FK_StylometryRunFeatures_Runs
                FOREIGN KEY (RunId) REFERENCES StylometryRuns(RunId) ON DELETE CASCADE
        );",

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
