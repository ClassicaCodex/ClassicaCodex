using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassicaCodex.Core.Meter;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Alphabet;
using ClassicaCodex.Mac.Features.Morphology;
using ClassicaCodex.Mac.Features.Vocabulary;
using ClassicaCodex.Mac.Features.WordStudy;
using ClassicaCodex.Mac.Shell;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// The lexical tools - Word Study, the work picker, the morphology search,
/// Core Vocabulary and the alphabet reference - driven against the sample
/// library, which carries the Aeneid and the Iliad with Latin and Greek lemma
/// data, Lewis and Short, and LSJ.
/// </summary>
public class LexicalTests
{
    [AvaloniaFact]
    public async Task Word_study_on_the_first_line_of_the_Aeneid_scans_it_and_finds_cano_in_Lewis_and_Short()
    {
        if (await OpenLibraryAsync() is not { } main) return;

        var (aeneid, passage) = await FirstLineAsync(main, "Aeneid");
        Assert.StartsWith("Arma virumque cano", passage.Node.Text);

        WordStudyFeature.Open(main, passage, "cano");
        var study = await Opened<WordStudyWindow>(main);
        await study.WhenIdleAsync();

        // The words of the line, and the one asked for already chosen.
        Assert.Equal(new[] { "Arma", "virumque", "cano", "Troiae", "qui", "primus", "ab", "oris" }, study.Words);
        Assert.Equal("cano", study.WordList.SelectedItem);

        // A Latin verse line, so the metre row is drawn, and on the chosen
        // word it gives that word's syllables: cano's final o is long by the
        // metre, which is the thing the spelling cannot say.
        Assert.True(study.HasMetreRow);
        Assert.Contains("cano", study.MetreText);
        Assert.Contains("¯", study.MetreText);
        Assert.Contains("long syllable", study.MetreText);

        // Its headwords, the verb among them, and Lewis and Short's entry for it.
        Assert.Contains(study.Headwords, h => h.Headword == "cano");
        study.HeadwordList.SelectedIndex = study.Headwords.ToList().FindIndex(h => h.Headword == "cano");
        await study.WhenIdleAsync();

        Assert.Contains("căno", study.DefinitionText);
        Assert.Contains("Lewis & Short", study.DefinitionText);
        Assert.Contains("Attested forms of cano", study.FormsText);

        // Scoped to the work the line came from, as on Windows.
        Assert.Equal("Searching 1 text", study.ScopeText);
        Assert.NotEmpty(study.Occurrences);
        Assert.All(study.Occurrences, o => Assert.Equal(aeneid.WorkId, o.WorkId));
        Assert.Contains("in the text searched", study.StatusText);

        await Screens.Settle(4);
        Screens.Save(study, "lexical-01-word-study-aeneid-light");

        // Widened to every text, the same headword turns up in Ovid too.
        await study.ApplyScopeAsync(Array.Empty<int>());
        Assert.Equal("Searching every text", study.ScopeText);
        Assert.Contains(study.Occurrences, o => o.AuthorName == "Ovid");
        Assert.Contains("across the corpus", study.StatusText);

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(6);
        Screens.Save(study, "lexical-02-word-study-aeneid-dark");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        // A different word: the line's own scansion comes back for a word the
        // metre has nothing on, and Arma finds its headword.
        study.WordList.SelectedItem = "Arma";
        await study.WhenIdleAsync();
        Assert.Contains(study.Headwords, h => h.Headword.Equals("arma", StringComparison.OrdinalIgnoreCase));

        // With no word chosen the row gives the line's feet, and says how many
        // readings survive rather than picking one: where the spelling leaves
        // the shape open, the reader is told the marks are only what the
        // readings agree on.
        study.WordList.SelectedItem = null;
        await study.WhenIdleAsync();
        var scansion = HexameterScanner.Scan(passage.Node.Text);
        Assert.True(scansion.Scans);
        Assert.Contains($"hexameter  {scansion.Pattern}", study.MetreText);
        Assert.Contains(scansion.ReadingCount == 1 ? "one reading" : $"{scansion.ReadingCount} readings, so the marks below show only what they agree on", study.MetreText);
        Assert.Empty(study.Headwords);

        study.Close();
        main.Close();
    }

    [AvaloniaFact]
    public async Task Word_study_on_the_first_line_of_the_Iliad_finds_menis_in_LSJ_and_draws_no_metre_row()
    {
        if (await OpenLibraryAsync() is not { } main) return;

        var (_, passage) = await FirstLineAsync(main, "Iliad");

        WordStudyFeature.Open(main, passage, "μῆνιν");
        var study = await Opened<WordStudyWindow>(main);
        await study.WhenIdleAsync();

        // The scanner reads Latin hexameter; a Greek line never asks the question.
        Assert.False(study.HasMetreRow);

        Assert.Contains(study.Headwords, h => h.Headword == "μῆνις");
        Assert.Contains("μῆνις", study.DefinitionText);
        Assert.Contains("wrath", study.DefinitionText);

        // LSJ's Beta Code is decoded, and its element-by-element line breaks
        // are joined back into prose.
        Assert.DoesNotContain("mh=nis", study.DefinitionText);
        Assert.DoesNotContain("Dor.\nand", study.DefinitionText);

        Assert.NotEmpty(study.Occurrences);
        Assert.All(study.Occurrences, o => Assert.Equal("Iliad", o.WorkTitle));

        await Screens.Settle(4);
        Screens.Save(study, "lexical-03-word-study-iliad-light");

        // Double-click hands the occurrence to the reader and closes Word
        // Study, as on Windows. The jump is recorded rather than made, so the
        // test is about this window and not the reader's scrolling.
        Assert.NotNull(study.OnNavigate);
        var index = Math.Min(3, study.Occurrences.Count - 1);
        var target = study.Occurrences[index];
        (int WorkId, long TextNodeId)? jumped = null;
        study.OnNavigate = (workId, textNodeId) => { jumped = (workId, textNodeId); return Task.CompletedTask; };
        await study.JumpToOccurrenceAsync(index);
        await Screens.Settle(2);
        Assert.Equal((target.WorkId, target.TextNodeId), jumped);
        Assert.False(study.IsVisible);

        main.Close();
    }

    [AvaloniaFact]
    public async Task The_work_picker_gathers_choices_across_filters_and_returns_them()
    {
        if (await OpenLibraryAsync() is not { } main) return;

        var authors = await new AuthorRepository().GetAllAsync();
        var works = await new WorkRepository().GetAllGroupedByAuthorAsync();
        var ovidCount = works[authors.First(a => a.Name == "Ovid").AuthorId].Count;

        var picker = new WorkPickerWindow(authors, works, Array.Empty<int>());
        picker.Show(main);
        await Screens.Settle(4);

        // Opened on "everything", which returns no filter at all.
        Assert.True(picker.AllWorksCheck.IsChecked);
        Assert.Empty(picker.SelectedWorkIds);
        Assert.Equal("Every text in the corpus.", picker.CountText);

        picker.AllWorksCheck.IsChecked = false;
        Assert.Equal("No texts chosen - nothing will be searched.", picker.CountText);

        // Narrow to an author, take the lot.
        picker.FilterBox.Text = "Ovid";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ovidCount, picker.ShownWorks.Count);
        picker.SetAllShown(true);
        Assert.Equal($"{ovidCount} text(s) chosen.", picker.CountText);

        // Another author, one play: the Ovid already chosen stays chosen
        // while it is filtered out of sight.
        picker.FilterBox.Text = "Agamemnon";
        Dispatcher.UIThread.RunJobs();
        Assert.Single(picker.ShownWorks);
        picker.ShownWorks[0].IsChecked = true;
        Assert.Equal($"{ovidCount + 1} text(s) chosen.", picker.CountText);

        // Chosen only shows the whole gathering once the filter is cleared.
        picker.FilterBox.Text = string.Empty;
        Dispatcher.UIThread.RunJobs();
        picker.SetShowMode(WorkPickerWindow.ShowMode.Chosen);
        Assert.Equal(ovidCount + 1, picker.ShownWorks.Count);

        await Screens.Settle(4);
        Screens.Save(picker, "lexical-04-work-picker");

        picker.Accept();
        Assert.True(picker.Accepted);
        Assert.Equal(ovidCount + 1, picker.SelectedWorkIds.Count);

        main.Close();
    }

    [AvaloniaFact]
    public async Task Morphology_finds_aorist_optatives_in_the_Iliad_and_says_what_Latin_tags_cannot_do()
    {
        if (await OpenLibraryAsync() is not { } main) return;

        var iliad = main.LibraryWorks.First(w => w.Title == "Iliad");

        MorphologyFeature.Open(main);
        var morphology = await Opened<MorphologyWindow>(main);
        await morphology.WhenIdleAsync();

        Assert.True(morphology.CanSearch);
        Assert.Contains("tagged forms available for Greek", morphology.StatusText);

        morphology.Choose("Part of speech", 'v');
        morphology.Choose("Tense", 'a');
        morphology.Choose("Mood", 'o');
        Assert.Equal("Tag patterns: v??ao????  /  v???ao????", morphology.PatternText);

        await morphology.ApplyScopeAsync(new[] { iliad.WorkId });
        await morphology.SearchAsync();

        Assert.NotEmpty(morphology.Results);
        Assert.All(morphology.Results, r => Assert.Equal("Iliad", r.WorkTitle));
        Assert.All(morphology.Results, r => Assert.True(r.Tag[0] == 'v'));
        Assert.Contains("Double-click one to jump to it.", morphology.StatusText);

        await Screens.Settle(4);
        Screens.Save(morphology, "lexical-05-morphology-light");

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(6);
        Screens.Save(morphology, "lexical-06-morphology-dark");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        // Latin is loaded but tagged in a coarser vocabulary, and the window
        // says so before anyone searches and wonders why nothing comes back.
        morphology.LanguageCombo.SelectedIndex = 1;
        await morphology.WhenIdleAsync();
        Assert.Contains("Latin tags use a different, coarser format", morphology.StatusText);

        // A jump hands the passage to the reader and closes the search, as on Windows.
        morphology.LanguageCombo.SelectedIndex = 0;
        await morphology.WhenIdleAsync();
        await morphology.SearchAsync();
        Assert.NotNull(morphology.OnNavigate);
        var first = morphology.Results[0];
        (int WorkId, long TextNodeId)? jumped = null;
        morphology.OnNavigate = (workId, textNodeId) => { jumped = (workId, textNodeId); return Task.CompletedTask; };
        await morphology.JumpToResultAsync(0);
        await Screens.Settle(2);
        Assert.Equal((first.WorkId, first.TextNodeId), jumped);
        Assert.False(morphology.IsVisible);

        main.Close();
    }

    [AvaloniaFact]
    public async Task Core_vocabulary_ranks_the_Aeneid_and_refuses_a_work_with_no_original()
    {
        if (await OpenLibraryAsync() is not { } main) return;

        var aeneid = main.LibraryWorks.First(w => w.Title == "Aeneid");
        await VocabularyFeature.OpenAsync(main, aeneid);
        var vocabulary = await Opened<VocabularyWindow>(main);
        await vocabulary.Building;

        Assert.NotNull(vocabulary.Result);
        Assert.True(vocabulary.Result!.Entries.Count > 1000);
        Assert.Contains("running words, built from", vocabulary.SummaryText);
        Assert.StartsWith("Learn the top", vocabulary.HeadlineText);
        Assert.Contains("four fifths", vocabulary.HeadlineText);

        // Ranked, with coverage climbing and never past the whole text.
        var entries = vocabulary.Result.Entries;
        Assert.Equal(1, entries[0].Rank);
        Assert.True(entries[0].Occurrences >= entries[^1].Occurrences);
        Assert.True(entries[^1].CumulativeCoverage <= 1.0);

        await Screens.Settle(6);
        Screens.Save(vocabulary, "lexical-07-vocabulary-light");

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(6);
        Screens.Save(vocabulary, "lexical-08-vocabulary-dark");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        vocabulary.Close();

        // A work with no original-language edition has nothing to count, and
        // the guard says so instead of opening an empty list.
        var opening = VocabularyFeature.OpenAsync(main, new Work { WorkId = -1, Title = "No such work" });
        await Screens.Settle(4);
        var alert = main.OwnedWindows.Single(w => w is not FeatureWindow && w.IsVisible);
        Screens.Save(alert, "lexical-09-vocabulary-no-original");
        alert.Close();
        await opening;
        Assert.DoesNotContain(main.OwnedWindows, w => w is VocabularyWindow && w.IsVisible);

        main.Close();
    }

    [AvaloniaFact]
    public async Task The_alphabet_reference_shows_Greek_Latin_or_says_it_has_nothing()
    {
        if (await OpenLibraryAsync() is not { } main) return;

        AlphabetFeature.Open(main, "grc");
        var alphabet = await Opened<AlphabetWindow>(main);
        Assert.Equal("The Greek Alphabet", alphabet.Title);
        Screens.Save(alphabet, "lexical-10-alphabet-greek");

        // The whole reference at once, marks and pairs included, for looking at.
        var height = alphabet.Height;
        alphabet.Height = 2100;
        await Screens.Settle(4);
        Screens.Save(alphabet, "lexical-10b-alphabet-greek-full");

        alphabet.ShowLanguage("lat");
        await Screens.Settle(4);
        Assert.Equal("The Latin Alphabet", alphabet.Title);
        Screens.Save(alphabet, "lexical-11-alphabet-latin");
        alphabet.Height = height;

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        alphabet.ShowLanguage("Greek");
        await Screens.Settle(6);
        Assert.Equal("grc", alphabet.Language);
        Screens.Save(alphabet, "lexical-12-alphabet-greek-dark");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        alphabet.ShowLanguage("eng");
        await Screens.Settle(4);
        Assert.Equal("Alphabet", alphabet.Title);

        alphabet.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void Lexicon_entries_are_reflowed_into_prose_with_a_paragraph_per_sense()
    {
        var reflowed = WordStudyWindow.Reflow("mh=nis ,\nDor.\nand\nAeol.\nma=n- , h( , gen.\nA.\nmh/nios\n, later");
        Assert.Equal("mh=nis , Dor. and Aeol. ma=n- , h( , gen.\n\nA. mh/nios , later", reflowed);

        Assert.Equal("căno , cĕcĭni (ancient\n\nI. imp. cante\n\nII. Act.",
            WordStudyWindow.Reflow("căno , cĕcĭni (ancient\nI. imp. cante\nII. Act."));
    }

    private static async Task<MainWindow?> OpenLibraryAsync()
    {
        if (Screens.SampleLibrary is not { } library) return null;

        DbConnectionFactory.Configure(library, remember: false);
        await SchemaInitializer.EnsureSchemaAsync();

        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var window = new MainWindow { Width = 1360, Height = 860 };
        window.Show();
        await Screens.Settle(10);
        return window;
    }

    /// <summary>Opens a work in the reader on its first line, and the passage as the line menu would hand it over.</summary>
    private static async Task<(Work Work, PassageContext Passage)> FirstLineAsync(MainWindow main, string title)
    {
        var work = main.LibraryWorks.First(w => w.Title == title);
        var edition = (await new EditionRepository().GetByWorkAsync(work.WorkId)).First(e => e.Kind == EditionKind.Original);
        var node = (await new TextNodeRepository().GetByEditionAsync(edition.EditionId, readingLinesOnly: true)).First();

        await main.NavigateToPassageAsync(work.WorkId, node.TextNodeId);
        await Screens.Settle(6);

        var passage = new PassageContext(node, edition, null, FromOriginal: true, "Palatino", null, null);
        return (work, passage);
    }

    private static async Task<T> Opened<T>(MainWindow main) where T : Window
    {
        await Screens.Settle(6);
        return main.OwnedWindows.OfType<T>().Last(w => w.IsVisible);
    }
}
