namespace ClassicaCodex.UI;

/// <summary>
/// A plain topic-and-text help window. Deliberately describes what the app
/// actually does - including the places where results are approximate - so a
/// new user isn't left guessing whether something is broken or simply
/// working as well as the source data allows.
/// </summary>
public class HelpForm : Form
{
    private readonly ListBox _topicList;
    private readonly TextBox _contentBox;

    private static readonly (string Title, string Body)[] Topics =
    {
        ("Getting started", """
Classica Codex reads the Perseus Digital Library - the Greek and Latin classics, their English translations, dictionaries, and the linguistic data that makes searching them work properly.

Everything starts in Setup Wizard, on the main toolbar. It asks how you'd like to set things up:

Guided Setup (recommended)
   One step at a time: first the database (where your library, tags, and bookmarks live), then each data source in turn, then the word index - plain language throughout, no file paths or repository URLs on screen. Every step after the database is optional; clicking Next without running one just moves on, so you can go straight through to Finish having only set up the database, then come back for the rest whenever you like.

   On a genuine first run - no database configured yet - this is what opens automatically, database step first.

Advanced Setup
   Everything on one screen instead: every data source as its own row with its own destination folder, downloaded and ingested independently, in whatever order suits. Already have the repositories downloaded yourself? Point straight at those folders instead of fetching them again - same result, no second copy.

Both do the exact same underlying work; Guided Setup just does it one thing at a time with plain-language explanations and lets you skip whatever you don't need yet.

Whichever you use, once the texts are in, build the word index (its own step in Guided Setup, its own section in Advanced Setup). This is what makes searching fast - without it, every search scans the whole corpus once per word form. It takes a few minutes and can be rerun at any time.

Downloads are large and take a while. Run one at a time and let each finish.
"""),

        ("Reading texts", """
Pick an author in the library tree on the left, then a work beneath it. The original-language text loads on the left, its translation on the right.

Two editions of the same thing
   The dropdown above each pane lists every edition of that work you have. Where a work has several translations, you can switch between translators freely - the other pane stays where it is.

Scrolling and selection are synced between the panes. That works best on verse, where a translation keeps roughly the same line structure as the original. Prose translations often carry far fewer citation points than the original does, so the two sides drift apart; that's how the texts were digitized, not a fault in the alignment.

Hover any line to see its citation reference.

Right-click a line for:
   Tag this line - file it under a name you choose
   Bookmark this line - save it with a note
   Find Echoes - look for passages elsewhere that share rare wording
   Reception History - the same, split into earlier and later authors
   Word Study - look the words up properly
   Export - write the passage out to a file
"""),

        ("Searching", """
The search box matches word forms, not just letters. Searching for a Greek or Latin word finds its other inflected forms too - so one search for a verb turns up every form of it in the corpus, not only the exact spelling typed.

That depends on the lemma data being loaded (Setup Wizard). Without it, search falls back to matching the literal text.

Results appear beneath the reader with the matched words highlighted. Double-click one to jump straight to it in context.
"""),

        ("Tags and the Myth Network", """
A tag is any label you attach to lines - a god, a hero, a place, a theme. Tag lines by hand from the reader's right-click menu, or in bulk:

Auto-Tag (inside Myth Network)
   Type a name, add any alternate spellings translators might use (Athene, Pallas, Minerva), and search the whole corpus at once. Every match is listed with its passage so you can uncheck anything wrong before committing - a name search on a proper noun does over-match sometimes, and won't catch a figure referred to only by epithet.

The Myth Network
   Tags become nodes; two tags are linked when they occur together. Circle size is how often you've used a tag, line thickness how strongly two co-occur.

   Co-occurrence has two settings. "Same work" links tags appearing anywhere in the same text - a weak signal once encyclopedic sources are loaded, since those mention nearly everyone. "Same passage" requires them to actually appear near each other, which is usually what you want. The window sets how near.

   Click a node for its passages; click a line between two nodes to see exactly which passages connect them.

   Shapes lets you give each tag category its own shape - gods as circles, heroes as squares, places as triangles.
"""),

        ("Word Study and dictionaries", """
Right-click a line and choose Word Study, then click any word in it.

You'll get the dictionary headwords that form could come from (sometimes more than one - that ambiguity is real and worth seeing rather than having it guessed away), the full paradigm of attested forms, the dictionary entry itself, and every occurrence across the corpus.

Dictionary entries come from Liddell-Scott-Jones for Greek and Lewis & Short for Latin. Load them from Setup Wizard if you haven't.
"""),

        ("Analysis tools", """
Concordance
   A keyword-in-context view: every occurrence of a word lined up with what comes before and after, so patterns of usage are visible at a glance.

Stylometry
   Compares writing style using Burrows's Delta, a standard authorship-attribution measure based on how often each author reaches for common function words. Runs on original-language texts, since it's comparing the actual words an author wrote.

Find Echoes
   Looks for passages sharing unusually rare words with the one you started from. Rare-word overlap is a much stronger signal of allusion than shared common words - two authors both using "and" means nothing. These are candidates worth a human look, not proof of borrowing.

   It only compares like with like: original against original, translation against translation. It can't spot an echo between a Greek original and an English translation of something else, since those aren't the same words.

Reception History
   The same search, sorted into authors who wrote earlier than your passage (who may have influenced it) and later (who may be echoing it). Authors whose dates aren't on record can't be placed and are listed separately.

Timeline
   Every dated author in your library on a chronological axis. Dates are rough consensus estimates, not settled fact. Click an author for their works.
"""),

        ("Exporting passages", """
Right-click any line and choose Export.

Scope
   A set number of lines from that point, everything to the end of the work, or the entire work from its beginning.

Options
   Show citation refs - the [1.1] markers, on or off
   Combine into one continuous passage - merge the lines into flowing text instead of one line per entry
   Include both original and translation - a bilingual document

Formats: plain text, Word (.docx), or PDF.

About bilingual export: the two editions are matched by citation reference, which is the only thing they genuinely share. Where a translation is divided more coarsely than the original - an English chapter covering a dozen Latin sections - it's matched to the section it covers and shown once. Introductions and cast lists that exist only in the translation are still included. The status line tells you how many lines actually paired.
"""),

        ("Appearance and settings", """
Dark Mode / Light Mode
   Toggles from the toolbar. Dark mode avoids pure black on pure white deliberately - maximum contrast is harder to read for long stretches, which rather defeats the point. Your choice is remembered.

Database Location
   Where the library, tags, and bookmarks are stored. Set it as the first step of Guided Setup, or from Advanced Setup's Database Location button - either one changes the same setting. Point it at a different file to keep entirely separate libraries. You're only asked for this on a first run, or if the file has been moved or deleted.
"""),

        ("Where your data lives", """
The database is a single SQLite file. By default:

   %LocalAppData%\\ClassicaCodex\\classicacodex.db

It holds everything you create - tags, bookmarks - alongside the ingested texts. Copying that one file backs up the lot, and it can be moved anywhere you like from Setup Wizard - the Database step in Guided Setup, or the Database Location button in Advanced Setup.

Downloaded source repositories default to:

   Documents\\ClassicaCodexData\\

Those are just working copies of public data. Deleting them costs nothing but the time to fetch them again; your tags and bookmarks live in the database, not in those folders.

Preferences (theme, category shapes, database location) sit in small files beside the database.

None of the texts, dictionaries, or linguistic data belong to this app - see About for the full attribution and licensing.
"""),
    };

    public HelpForm()
    {
        Text = "Classica Codex - Help";
        AppIcons.ApplyWindowIcon(this, "Help");
        Width = 900;
        Height = 640;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(700, 460);

        _topicList = new ListBox
        {
            Left = 12,
            Top = 12,
            Width = 210,
            Height = 570,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
            IntegralHeight = false
        };
        foreach (var (title, _) in Topics) _topicList.Items.Add(title);
        _topicList.SelectedIndexChanged += (_, _) => ShowSelectedTopic();

        _contentBox = new TextBox
        {
            Left = 234,
            Top = 12,
            Width = 640,
            Height = 570,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Georgia", 10.5F),
            WordWrap = true
        };

        Controls.Add(_topicList);
        Controls.Add(_contentBox);

        Load += (_, _) =>
        {
            _topicList.SelectedIndex = 0;
            ShowSelectedTopic();
        };

        ReadingTheme.AttachTo(this);
    }

    private void ShowSelectedTopic()
    {
        var index = _topicList.SelectedIndex;
        if (index < 0 || index >= Topics.Length) return;

        // Normalized to CRLF because a WinForms multiline TextBox renders a
        // bare LF as a box rather than a line break.
        _contentBox.Text = Topics[index].Body.Trim().ReplaceLineEndings("\r\n");
        _contentBox.SelectionStart = 0;
        _contentBox.ScrollToCaret();
    }
}
