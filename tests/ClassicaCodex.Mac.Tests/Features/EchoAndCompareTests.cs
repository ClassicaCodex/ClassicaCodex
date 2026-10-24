using System.Globalization;
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Compare;
using ClassicaCodex.Mac.Features.Echoes;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// Find Echoes, Reception History, Cross-Language Echo, Compare Translations
/// and Collate Editions, driven against the sample library.
/// </summary>
public class EchoAndCompareTests
{
    [AvaloniaFact]
    public async Task Find_echoes_ranks_candidates_for_Aeneid_6_851()
    {
        if (await OpenSampleAsync() is not { } main) return;

        try
        {
            var passage = await PassageAsync(main, "Aeneid", EditionKind.Original, "6.851");
            Assert.StartsWith("tu regere imperio populos", passage.Node.Text);

            EchoFeature.FindEchoes(main, passage);
            var window = await OwnedAsync<EchoResultsWindow>(main);
            await window.LoadTask!;
            await Screens.Settle();

            var results = window.Results;
            Assert.NotEmpty(results);
            Assert.DoesNotContain(results, r => r.TextNodeId == passage.Node.TextNodeId);
            Assert.All(results, r => Assert.True(r.SharedWordCount > 0));
            Assert.Equal(results.OrderByDescending(r => r.SharedWordCount).Select(r => r.SharedWordCount), results.Select(r => r.SharedWordCount));
            Assert.Null(window.ResultList.Message);
            Assert.Equal(results.Count, window.ResultList.Hits.Count);

            // Original against originals only: the Aeneid's line is Latin, so
            // nothing English can be among the candidates.
            var editions = new TextNodeRepository();
            var kinds = await editions.GetEditionIdsAsync(results.Select(r => r.TextNodeId).ToList());
            var originalIds = (await new EditionRepository().GetAllOriginalEditionsAsync()).Select(e => e.EditionId).ToHashSet();
            Assert.All(kinds.Values, id => Assert.Contains(id, originalIds));

            // The pasteboard line carries everything the row hides.
            var first = results[0];
            Assert.Equal($"{first.AuthorName}, {first.WorkTitle} [{PassageCitation.Display(first.CitationRef, first.Milestone)}]: {first.Text}", first.ClipboardText);

            Screens.Save(window, "echo-01-find-echoes-aeneid-light");
            await InDarkAsync(() => Screens.Save(window, "echo-02-find-echoes-aeneid-dark"));
            window.Close();
        }
        finally
        {
            main.Close();
        }

        // And the Iliad's opening line, in Greek - in a reader of its own,
        // since the shared reader pane cannot yet be switched from one open
        // work to another without throwing (see the note on PassageAsync).
        main = (await OpenSampleAsync())!;
        try
        {
            var iliad = await PassageAsync(main, "Iliad", EditionKind.Original, "1.1");
            EchoFeature.FindEchoes(main, iliad);
            var greek = await OwnedAsync<EchoResultsWindow>(main);
            await greek.LoadTask!;
            await Screens.Settle();
            Assert.NotEmpty(greek.Results);
            Assert.All(greek.Results, r => Assert.True(r.SharedWordCount > 0));
            Screens.Save(greek, "echo-03-find-echoes-iliad");
            greek.Close();
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public async Task Activating_a_candidate_asks_the_reader_to_jump_to_it()
    {
        if (await OpenSampleAsync() is not { } main) return;

        try
        {
            var passage = await PassageAsync(main, "Aeneid", EditionKind.Original, "6.851");
            EchoFeature.FindEchoes(main, passage);
            var window = await OwnedAsync<EchoResultsWindow>(main);
            await window.LoadTask!;

            // Wired to the reader's own navigation...
            Assert.Same(main, window.OnNavigate!.Target);
            Assert.Equal(nameof(MainWindow.NavigateToPassageAsync), window.OnNavigate.Method.Name);

            // ...and called with the candidate's work and passage. Recorded
            // rather than carried out: the shared reader pane throws when its
            // list recycles rows for a distant line (see the note on
            // PassageAsync), and that is not this window's behaviour to test.
            var jumps = new List<(int WorkId, long TextNodeId)>();
            window.OnNavigate = (work, node) => { jumps.Add((work, node)); return Task.CompletedTask; };

            var target = window.Results[^1];
            window.ResultList.List.SelectedItem = target;
            await window.ResultList.Activated!(target);
            await Screens.Settle();

            Assert.Equal(new[] { (target.WorkId, target.TextNodeId) }, jumps);
            Assert.True(window.IsVisible, "The candidates stay open beside the reader after a jump.");
            window.Close();
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public async Task Reception_history_splits_the_candidates_by_their_authors_dates()
    {
        if (await OpenSampleAsync() is not { } main) return;

        try
        {
            var passage = await PassageAsync(main, "Aeneid", EditionKind.Original, "6.851");
            EchoFeature.ReceptionHistory(main, passage);
            var window = await OwnedAsync<ReceptionTrackerWindow>(main);
            await window.LoadTask!;
            await Screens.Settle();

            var all = await new TextNodeRepository().FindEchoesAsync(passage.Node.TextNodeId);
            Assert.Equal(all.Count, window.Later.Count + window.Earlier.Count + window.Unknown.Count);

            var virgil = AuthorEraData.Lookup(main.AuthorNameFor(main.OpenWork!));
            Assert.NotNull(virgil);
            Assert.All(window.Later, h => Assert.True(AuthorEraData.Lookup(h.AuthorName)!.Value.StartYear > virgil.Value.StartYear));
            Assert.All(window.Earlier, h => Assert.True(AuthorEraData.Lookup(h.AuthorName)!.Value.StartYear < virgil.Value.StartYear));
            Assert.NotEmpty(window.Later.Concat(window.Earlier));

            Screens.Save(window, "echo-04-reception-aeneid-light");
            await InDarkAsync(() => Screens.Save(window, "echo-05-reception-aeneid-dark"));
            window.Close();
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public async Task Cross_language_echo_lists_the_other_originals_and_asks_for_a_key_first()
    {
        if (await OpenSampleAsync() is not { } main) return;

        try
        {
            var passage = await PassageAsync(main, "Aeneid", EditionKind.Original, "6.851");
            await EchoFeature.CrossLanguageAsync(main, passage);
            var window = await OwnedAsync<CrossLanguageEchoWindow>(main);
            await window.LoadTask!;
            await Screens.Settle();

            var options = window.WorkList.Items.Cast<object>().Select(o => o.ToString()!).ToList();
            var originals = await new EditionRepository().GetAllOriginalEditionsAsync();
            Assert.Equal(originals.Count - 1, options.Count);
            Assert.DoesNotContain(options, o => o.Contains("Aeneid"));
            Assert.Contains(options, o => o.StartsWith("Homer — Iliad (GRC)", StringComparison.Ordinal));

            // No Gemini key on this machine: the button offers to set one up,
            // and stays usable for that even with no work chosen.
            if (string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey))
            {
                Assert.Equal("Configure Gemini Key…", window.FindButtonText);
                Assert.True(window.FindButton.IsEnabled);
            }

            window.WorkFilter.Text = "Homer";
            window.FlushWorkFilter();
            var filtered = window.WorkList.Items.Cast<object>().Select(o => o.ToString()!).ToList();
            Assert.NotEmpty(filtered);
            Assert.All(filtered, o => Assert.Contains("Homer", o));

            // With a key, the button follows the choice of work.
            WithGeminiKey(() =>
            {
                window.WorkList.SelectedIndex = 0;
                Assert.Equal("Find Echoes with Gemini (free)", window.FindButtonText);
                Assert.True(window.FindButton.IsEnabled);

                window.WorkList.SelectedIndex = -1;
                Assert.Equal("Pick a work above first", window.FindButtonText);
                Assert.False(window.FindButton.IsEnabled);
            });

            window.WorkList.SelectedIndex = filtered.FindIndex(o => o.Contains("Iliad"));
            await Screens.Settle();
            Screens.Save(window, "echo-06-cross-language-no-key");
            window.Close();
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public async Task Cross_language_echo_will_not_spend_a_request_on_a_speaker_name()
    {
        if (await OpenSampleAsync() is not { } main) return;

        try
        {
            var agamemnon = main.LibraryWorks.First(w => w.Title == "Agamemnon");
            var edition = (await new EditionRepository().GetByWorkAsync(agamemnon.WorkId)).First(e => e.Kind == EditionKind.Original);
            var speaker = (await new TextNodeRepository().GetByEditionAsync(edition.EditionId)).First(n => n.NodeKind == TextNodeKinds.Speaker);

            var window = new CrossLanguageEchoWindow(speaker, "grc", "Aeschylus", "Agamemnon", edition.EditionId, EchoUi.SourceFont);
            window.Show(main);
            await Screens.Settle();
            await window.LoadTask!;

            WithGeminiKey(() =>
            {
                window.WorkList.SelectedIndex = 0;
                Assert.Equal("Speakers can't be echoed - pick a line of text", window.FindButtonText);
                Assert.False(window.FindButton.IsEnabled);
            });

            window.Close();
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public async Task Cross_language_checks_drop_invented_citations_and_flag_misplaced_evidence()
    {
        if (await OpenSampleAsync() is not { } main) return;

        try
        {
            var odyssey = main.LibraryWorks.First(w => w.Title == "Odyssey");
            var edition = (await new EditionRepository().GetByWorkAsync(odyssey.WorkId)).First(e => e.Kind == EditionKind.Original);
            var nodes = await new TextNodeRepository().GetByEditionAsync(edition.EditionId, readingLinesOnly: true);
            var line3 = nodes.First(n => PassageCitation.Display(n.CitationRef) == "1.3");
            var line1 = nodes.First(n => PassageCitation.Display(n.CitationRef) == "1.1");

            // The whole Odyssey is more than one request carries: the text
            // stops under the cap, at a whole line, and says which.
            var (tagged, truncatedAt) = CrossLanguageEchoCheck.BuildTaggedComparisonText(nodes);
            Assert.StartsWith($"[{nodes[0].CitationRef}] ", tagged);
            Assert.True(tagged.Length <= CrossLanguageEchoCheck.MaxComparisonChars);
            Assert.NotNull(truncatedAt);
            Assert.EndsWith($"[{truncatedAt}] {nodes.First(n => n.CitationRef == truncatedAt).Text}\n", tagged);

            // The model is asked to cite "exactly as tagged", brackets and all.
            var candidates = new[]
            {
                new EchoCandidate($"[{line3.CitationRef}]", "High", "Both open on remembering: 'μνήσατο' and 'ἔννεπε' frame the line."),
                new EchoCandidate(line1.CitationRef, "Medium", "The invocation of the Muse, 'Μοῦσα', asks her to tell."),
                new EchoCandidate("urn:cts:greekLit:tlg0012.tlg002.perseus-grc2.99.999", "Low", "Not a line of this work.")
            };

            var source = "þau er fremst um man";
            var verification = CrossLanguageEchoCheck.Verify(candidates, nodes, source);

            Assert.Equal(2, verification.Verified.Count);
            Assert.Equal(1, verification.UnresolvedCount);
            Assert.Equal(line3.TextNodeId, verification.Verified[0].Node.TextNodeId);

            // The words offered for 1.3 are at 1.29 and 1.1, not in 1.3 itself.
            Assert.Contains("μνήσατο", verification.Verified[0].UnsupportedWords);
            Assert.Contains("ἔννεπε", verification.Verified[0].UnsupportedWords);
            Assert.Empty(verification.Verified[1].UnsupportedWords);
            Assert.Equal(1, verification.UnsupportedCount);

            var summary = CrossLanguageEchoCheck.Summary(verification, truncatedAt);
            Assert.Contains($"Only compared through [{truncatedAt}] - the work is longer than this tool sends in one request.", summary);
            Assert.Contains("2 verified candidate(s).", summary);
            Assert.Contains("1 marked (!)", summary);
            Assert.Contains("1 citation(s) Gemini mentioned didn't match anything in this work", summary);
            Assert.Contains("(!) μνήσατο, ἔννεπε are not in this line", CrossLanguageEchoCheck.Flag(verification.Verified[0].UnsupportedWords));

            // An apostrophe inside a word opens no quotation.
            Assert.Equal(new[] { "Μοῦσα" }, CrossLanguageEchoCheck.QuotedSpans("The Odyssey's theme: 'Μοῦσα'"));

            // How the checked candidates look in the window.
            var vǫluspá = new TextNode { Text = "Hljóðs bið ek allar helgar kindir, meiri ok minni mögu Heimdallar; " + source, CitationRef = "1", NodeKind = TextNodeKinds.Line };
            var window = new CrossLanguageEchoWindow(vǫluspá, "non", "Anonymous", "Vǫluspá", -1, EchoUi.SourceFont);
            window.Show(main);
            await Screens.Settle();
            await window.LoadTask!;
            window.ShowVerification(verification, truncatedAt);
            await Screens.Settle();
            Screens.Save(window, "echo-07-cross-language-checked-light");
            await InDarkAsync(() => Screens.Save(window, "echo-08-cross-language-checked-dark"));
            window.Close();
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public async Task Echo_results_export_as_a_document_with_their_attribution()
    {
        if (await OpenSampleAsync() is not { } main) return;

        try
        {
            var passage = await PassageAsync(main, "Iliad", EditionKind.Original, "1.1");
            var hits = EchoHit.From(await new TextNodeRepository().FindEchoesAsync(passage.Node.TextNodeId));
            Assert.NotEmpty(hits);

            var window = new PassageSetExportWindow("Intertextual echoes", hits.Select(h => h.ToExport()).ToList(), null);
            window.Show(main);
            await Screens.Settle();

            var preview = window.PreviewText;
            foreach (var work in hits.Select(h => $"{h.AuthorName}, {h.WorkTitle}").Distinct())
                Assert.Contains(work, preview);
            Assert.Contains($"[{PassageCitation.Display(hits[0].CitationRef, hits[0].Milestone)}]", preview);
            Screens.Save(window, "echo-09-export-passages");
            window.Close();

            var folder = Path.Combine(Path.GetTempPath(), "cc-echo-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var chunks = hits.Select(h => ($"[{PassageCitation.Display(h.CitationRef, h.Milestone)}]", h.Text)).ToList();
                PassageSetExportWindow.Write(Path.Combine(folder, "echoes.txt"), "txt", "Intertextual echoes", chunks);
                PassageSetExportWindow.Write(Path.Combine(folder, "echoes.docx"), "docx", "Intertextual echoes", chunks);

                var text = await File.ReadAllTextAsync(Path.Combine(folder, "echoes.txt"));
                Assert.StartsWith("Intertextual echoes", text);
                Assert.Contains(hits[0].Text, text);
                Assert.True(new FileInfo(Path.Combine(folder, "echoes.docx")).Length > 0);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public async Task Compare_translations_sets_two_renderings_of_the_Iliad_side_by_side()
    {
        if (await OpenSampleAsync() is not { } main) return;

        try
        {
            var iliad = main.LibraryWorks.First(w => w.Title == "Iliad");
            Assert.True(await main.OpenWorkAsync(iliad.WorkId));
            await Screens.Settle();

            CompareFeature.CompareTranslations(main);
            var window = await OwnedAsync<CompareTranslationsWindow>(main, width: 1240, height: 800);
            await window.LoadTask!;
            await Screens.Settle();

            // The open work is chosen to begin with, and its translations ticked.
            var chosen = Assert.IsType<CompareTranslationsWindow.WorkChoice>(window.WorkList.SelectedItem);
            Assert.Equal(iliad.WorkId, chosen.WorkId);
            await window.TranslationsTask!;
            Assert.Equal(2, window.TranslationChecks.Count);
            Assert.All(window.TranslationChecks, c => Assert.True(c.IsChecked));

            await window.CompareAsync();
            await Screens.Settle();

            Assert.Equal(2, window.Columns.Count);
            Assert.Equal(new[] { "Augustus Taber Murray", "Samuel Butler" }, window.Columns.Select(c => c.Header).OrderBy(h => h));
            Assert.All(window.Columns, c => Assert.NotEmpty(c.Nodes));
            Assert.Contains(window.Columns, c => c.Nodes[0].Text.Contains("wrath", StringComparison.OrdinalIgnoreCase)
                                                  || c.Nodes[0].Text.Contains("anger", StringComparison.OrdinalIgnoreCase));

            Screens.Save(window, "compare-01-translations-iliad-light");
            await InDarkAsync(() => Screens.Save(window, "compare-02-translations-iliad-dark"));

            // Narrow enough that two equal shares would be too thin to read:
            // the columns keep their width and the page scrolls instead.
            window.Width = 760;
            await Screens.Settle();
            Screens.Save(window, "compare-03-translations-narrow");
            window.Close();
        }
        finally
        {
            main.Close();
        }
    }

    [Fact]
    public void An_AI_translation_is_told_apart_by_when_it_was_made()
    {
        var ai = new Edition { CtsUrn = "tlg0012.tlg001.ai-gemini-20260808193042", Translator = "Gemini (AI-generated)", Kind = EditionKind.Translation };
        var expected = new DateTime(2026, 8, 8, 19, 30, 42, DateTimeKind.Utc).ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);
        Assert.Equal($"Gemini (AI-generated) - {expected}", CompareTranslationsWindow.TranslatorLabel(ai));

        var anonymous = new Edition { CtsUrn = "phi0959.phi001.perseus-eng2", Kind = EditionKind.Translation };
        Assert.Equal("perseus-eng2", CompareTranslationsWindow.TranslatorLabel(anonymous));
    }

    [AvaloniaFact]
    public async Task Collation_says_so_when_no_work_has_two_stamped_editions()
    {
        if (await OpenSampleAsync() is not { } main) return;

        try
        {
            // The sample library's editions carry no collection, and a pairing
            // is only made between editions that came from collections.
            CompareFeature.Collate(main);
            var window = await OwnedAsync<CollationWindow>(main);
            await window.LoadTask!;
            await Screens.Settle();

            if (window.Pairs.Count == 0)
            {
                Assert.StartsWith("No work in this library has two original-language editions", window.SummaryText);
                Assert.False(window.ShowBox.IsEnabled);
                Screens.Save(window, "collate-01-nothing-to-collate");
            }

            window.Close();
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public async Task Collation_grades_the_differences_between_two_editions()
    {
        if (Screens.SampleLibrary is not { } sample) return;

        var copy = Path.Combine(Path.GetTempPath(), $"cc-collation-{Guid.NewGuid():N}.db");
        File.Copy(sample, copy);
        File.SetAttributes(copy, FileAttributes.Normal);

        MainWindow? main = null;
        try
        {
            DbConnectionFactory.Configure(copy, remember: false);
            await SchemaInitializer.EnsureSchemaAsync();
            var expected = await MakeSecondAgamemnonAsync();

            main = await OpenMainAsync();

            CompareFeature.Collate(main);
            var window = await OwnedAsync<CollationWindow>(main, width: 1200, height: 800);
            await window.LoadTask!;
            await Screens.Settle();

            // Two pairings: Caesar's Civil War in two Latin editions, and the
            // second Agamemnon made above.
            Assert.Equal(2, window.Pairs.Count);
            var labels = window.PairBox.Items.Cast<object>().Select(o => o.ToString()!).ToList();
            Assert.Contains(labels, l => l.StartsWith("Aeschylus - Agamemnon", StringComparison.Ordinal));
            Assert.Contains(labels, l => l.StartsWith("Julius Caesar - Civil War", StringComparison.Ordinal));

            // The two Civil Wars divide the work differently, and the window
            // says so rather than inventing a thousand variants.
            window.PairBox.SelectedIndex = labels.FindIndex(l => l.Contains("Civil War"));
            await window.CollateTask!;
            await Screens.Settle();
            Assert.False(window.Result!.IsAlignable);
            Assert.StartsWith("These two editions share no passage references", window.SummaryText);
            Assert.Contains(window.ExportNoteLines, n => n.StartsWith("These editions are not alignable"));

            window.PairBox.SelectedIndex = labels.FindIndex(l => l.Contains("Agamemnon"));
            await window.CollateTask!;
            await Screens.Settle();

            var result = window.Result!;
            Assert.True(result.IsAlignable);
            Assert.Equal(expected.Punctuation, result.PresentationDiffers);
            Assert.Equal(expected.Spelling, result.OrthographyDiffers);
            Assert.Equal(expected.Words, result.TextDiffers);
            Assert.Equal(0, result.LineationDiffers);
            Assert.Equal(1, result.OnlyInLeft);
            Assert.Equal(0, result.OnlyInRight);
            Assert.Equal(result.Shared - expected.Punctuation - expected.Spelling - expected.Words, result.Identical);
            Assert.StartsWith($"{result.Shared:N0} shared passages:  {result.Identical:N0} identical,  {expected.Punctuation} punctuation or spacing,  {expected.Spelling} spelling,  0 line division,  {expected.Words} in the words.", window.SummaryText);
            Assert.False(window.WholesaleNoticeShown);

            // Opening on the differences in the words, and the one passage
            // the second edition lacks.
            var shown = window.Rows.ItemsSource!.Cast<CollationWindow.RowView>().ToList();
            Assert.Equal(expected.Words + 1, shown.Count);
            Assert.Equal("THE WORDS", shown[0].Difference);
            Assert.Contains(shown, r => r.Difference == "only on the left" && r.RightCell == "—");
            Assert.Equal(shown[0].Row.Left, window.LeftDetailText);

            // Exported as shown, with no dash for an absence, and with notes
            // that say what the table is.
            var table = window.ExportTable;
            Assert.Equal(shown.Count + 1, table.Count);
            Assert.Equal(new[] { "Passage", "Difference" }, table[0].Take(2));
            Assert.Contains(table, r => r[1] == "only on the left" && r[3] == string.Empty);
            Assert.Contains(window.ExportNoteLines, n => n.StartsWith("Showing: Differences in the words."));
            Assert.Contains(window.ExportNoteLines, n => n.Contains($"{expected.Words} in the words."));

            var csv = Path.ChangeExtension(copy, ".csv");
            TableExport.Write(csv, table, window.ExportNoteLines, "csv");
            var written = await File.ReadAllTextAsync(csv);
            Assert.StartsWith("# Classica Codex collation", written);
            File.Delete(csv);

            Screens.Save(window, "collate-02-agamemnon-words-light");

            window.ShowBox.SelectedIndex = 1;
            await Screens.Settle();
            Assert.Equal(expected.Punctuation + expected.Spelling + expected.Words + 1, window.Rows.ItemsSource!.Cast<object>().Count());
            await InDarkAsync(() => Screens.Save(window, "collate-03-agamemnon-every-difference-dark"));

            window.ShowBox.SelectedIndex = 2;
            await Screens.Settle();
            Assert.Equal(result.Rows.Count, window.Rows.ItemsSource!.Cast<object>().Count());
            window.Close();
        }
        finally
        {
            main?.Close();
            SqliteConnection.ClearAllPools();
            if (Screens.SampleLibrary is { } again) DbConnectionFactory.Configure(again, remember: false);
            File.Delete(copy);
        }
    }

    /// <summary>
    /// A second printing of Agamemnon in a copy of the library: the Perseus
    /// text with a known handful of changes of each grade - commas added, the
    /// accents taken off, a word added, one passage left out - so the
    /// collation's counts can be checked exactly. Both editions are stamped
    /// with a collection, as setup would stamp them; so is Caesar's Civil War,
    /// which the sample library genuinely holds twice.
    /// </summary>
    private static async Task<(int Punctuation, int Spelling, int Words)> MakeSecondAgamemnonAsync()
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync();

        async Task<object?> Scalar(string sql, params (string Name, object Value)[] args)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
            return await cmd.ExecuteScalarAsync();
        }

        var original = Convert.ToInt32(await Scalar(
            "SELECT e.EditionId FROM Editions e JOIN Works w ON w.WorkId = e.WorkId WHERE w.Title = 'Agamemnon' AND e.Kind = 'Original';"));

        await Scalar("UPDATE Editions SET Collection = 'perseus-greek' WHERE EditionId = @Id;", ("@Id", original));
        await Scalar(@"UPDATE Editions SET Collection = 'perseus-latin' WHERE Kind = 'Original' AND WorkId =
                         (SELECT WorkId FROM Works WHERE Title = 'Civil War');");

        var second = Convert.ToInt32(await Scalar(@"
            INSERT INTO Editions (WorkId, CtsUrn, Kind, Language, Translator, SourcePath, Orthography, Collection)
            SELECT WorkId, 'tlg0085.tlg005.test-grc9', Kind, Language, NULL, NULL, Orthography, 'perseus-greek'
            FROM Editions WHERE EditionId = @Id;
            SELECT last_insert_rowid();", ("@Id", original)));

        await Scalar(@"
            INSERT INTO TextNodes (EditionId, CitationRef, SortOrder, Text, IsAthetized, NodeKind, IsVerse, Milestone)
            SELECT @Second, REPLACE(CitationRef, 'perseus-grc2', 'test-grc9'), SortOrder, Text, IsAthetized, NodeKind, IsVerse, Milestone
            FROM TextNodes WHERE EditionId = @Id;", ("@Second", second), ("@Id", original));

        // Lines whose reference is theirs alone, long enough to carry accents,
        // spaced well apart so no two changed lines are neighbours and none
        // can be read as one line broken in two places.
        var lines = new List<(long Id, string Text)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT TextNodeId, Text FROM TextNodes
                WHERE EditionId = @Second AND NodeKind = 'line' AND LENGTH(Text) > 25
                  AND CitationRef IN (SELECT CitationRef FROM TextNodes WHERE EditionId = @Second GROUP BY CitationRef HAVING COUNT(*) = 1)
                ORDER BY SortOrder;";
            cmd.Parameters.AddWithValue("@Second", second);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) lines.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        async Task Rewrite(int index, string text) =>
            await Scalar("UPDATE TextNodes SET Text = @Text WHERE TextNodeId = @Id;", ("@Text", text), ("@Id", lines[index].Id));

        static string WithoutAccents(string text) =>
            new string(text.Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
                .Normalize(NormalizationForm.FormC);

        foreach (var i in new[] { 10, 20, 30 }) await Rewrite(i, lines[i].Text + ",");
        foreach (var i in new[] { 40, 50 }) await Rewrite(i, WithoutAccents(lines[i].Text));
        foreach (var i in new[] { 60, 70, 80, 90 }) await Rewrite(i, lines[i].Text + " καινόν");
        await Scalar("DELETE FROM TextNodes WHERE TextNodeId = @Id;", ("@Id", lines[100].Id));

        return (3, 2, 4);
    }

    // ---------------------------------------------------------------------

    private static async Task<MainWindow?> OpenSampleAsync()
    {
        if (Screens.SampleLibrary is not { } library) return null;

        DbConnectionFactory.Configure(library, remember: false);
        await SchemaInitializer.EnsureSchemaAsync();
        return await OpenMainAsync();
    }

    private static async Task<MainWindow> OpenMainAsync()
    {
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var main = new MainWindow { Width = 1360, Height = 860 };
        main.Show();
        await Screens.Settle(10);
        return main;
    }

    /// <summary>
    /// A line of a work as the reader would hand it to a tool: the passage,
    /// its edition and the edition opposite. The work is opened in the reader
    /// first, as it would be.
    ///
    /// One work per reader window in these tests, and no jumps far into one.
    /// The shared reader pane's row template reaches into its item, and
    /// Avalonia 12 builds that template once more with a null item when it
    /// clears rows it is recycling - so opening a second work in the same
    /// window, or jumping to a distant line, throws from ReaderPane.BuildRow.
    /// That is the shell's to fix; these tests keep out of its way rather
    /// than fail on it.
    /// </summary>
    private static async Task<PassageContext> PassageAsync(MainWindow main, string title, EditionKind kind, string reference)
    {
        var work = main.LibraryWorks.First(w => w.Title == title);
        Assert.True(await main.OpenWorkAsync(work.WorkId));

        var editions = await new EditionRepository().GetByWorkAsync(work.WorkId);
        var edition = editions.First(e => e.Kind == kind);
        var counterpart = editions.FirstOrDefault(e => e.Kind != kind);
        var nodes = await new TextNodeRepository().GetByEditionAsync(edition.EditionId);
        var node = nodes.First(n => PassageCitation.Display(n.CitationRef, n.Milestone) == reference);

        return new PassageContext(
            node, edition, counterpart, kind == EditionKind.Original, "Palatino",
            EditionLabels.Descriptor(edition), counterpart == null ? null : EditionLabels.Descriptor(counterpart));
    }

    private static async Task<T> OwnedAsync<T>(Window main, double? width = null, double? height = null) where T : Window
    {
        await Screens.Settle();
        var window = main.OwnedWindows.OfType<T>().Last();
        if (width != null) window.Width = width.Value;
        if (height != null) window.Height = height.Value;
        await Screens.Settle();
        return window;
    }

    private static async Task InDarkAsync(Action photograph)
    {
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        try
        {
            await Screens.Settle();
            photograph();
        }
        finally
        {
            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            await Screens.Settle(2);
        }
    }

    /// <summary>
    /// Runs <paramref name="check"/> as if a Gemini key were saved, without
    /// touching the keychain - and without an await inside, so nothing else
    /// can run while the pretend key is in place.
    /// </summary>
    private static void WithGeminiKey(Action check)
    {
        var cache = typeof(TranslationSettings).GetField("_cached", BindingFlags.NonPublic | BindingFlags.Static)!;
        var saved = cache.GetValue(null);
        cache.SetValue(null, ((string?)null, (string?)"test-key", true));
        try
        {
            check();
        }
        finally
        {
            cache.SetValue(null, saved);
        }
    }
}
