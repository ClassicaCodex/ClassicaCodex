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
Classica Codex reads the Perseus Digital Library - the Greek and Latin classics, their English translations, dictionaries, and the linguistic data that makes searching them work properly - plus two optional collections that extend it further: Shakespeare and the other Renaissance writers who reworked classical material in English, and Greek writing from after the classical period into late antiquity.

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
   The dropdown above each pane lists every edition of that work you have. Where a work has several translations, you can switch between translators freely - the other pane stays where it is. The original-language side can carry more than one edition too, when a later collection added an alternate older edition of a work Perseus already had (a couple of Sophocles' plays, among others) - each entry in the dropdown names which.

Scrolling and selection are synced between the panes. That works best on verse, where a translation keeps roughly the same line structure as the original. Prose translations often carry far fewer citation points than the original does, so the two sides drift apart; that's how the texts were digitized, not a fault in the alignment.

Hover any line to see its citation reference.

Right-click a line for:
   Copy to Clipboard - the line's text on its own
   Tag this line - file it under a name you choose
   Bookmark this line - save it with a note
   Find Echoes - look for passages elsewhere that share rare wording
   Find Cross-Language Echo - the same idea across languages (see Translate)
   Reception History - the same, split into earlier and later authors
   Translate - look up, generate, or listen to an English rendering of the line
   Word Study - look the words up properly
   Export - write the passage out to a file

Right-click a work in the library tree on the left for:
   View Details - everything known about the work and its editions
   Create Translation - renders a whole work at once rather than a single line, see Translate

View Details
   The catalogue entry for the work and each edition of it you have loaded - CTS URNs, language, translator, line counts - and, read straight from each edition's source file, the publication details its TEI header carries: who edited it, which printed edition it was digitised from, the publisher and year, and the licence.

   That printed source line is usually the one worth having. "Homer, Homeri Opera, David B. Monro, Thomas W. Allen, Oxford, Clarendon Press, 1920" tells you exactly which text you're reading, which matters before quoting it anywhere.

   This is read from the file each time you ask rather than stored in the library, so it needs the corpus files still to be where setup put them. If you've since deleted them the reader carries on working perfectly and this view simply says the header isn't available for that edition.

Where you left off
   The app reopens the last passage you were reading. It is remembered as the work's CTS URN and the citation reference rather than as an internal identifier, so it still points at the same passage after a corpus is re-ingested - and if that work or line is no longer there, the app simply opens as it always did rather than complaining.

   Only clicking a line updates it. Jumping to a search result or a tagged passage moves the reader without changing where you left off, so following a reference doesn't cost you your place.

   If reopening a long work at launch feels slow, turn off "Open where I last left off" under Reading in the Setup Wizard. Your place is still recorded either way, so switching it back on picks up where you were rather than starting over.

View Preface
   Some translations carry a translator's preface or similar front matter. It has nothing on the original side to line up against, so it's kept out of the reader rather than sitting at the top of the text looking like the first line of the work. Where an edition has one, "View Preface..." appears on that pane's right-click menu.
"""),

        ("Translate", """
Right-click a line and choose Translate, just above Word Study.

Ingested Translation
   If a translation edition of the work is loaded in the translation pane, this looks up the same passage in it automatically - the same citation matching Export's bilingual mode already uses, so it still lines up correctly even where the translation divides its text more coarsely than the original.

AI Translation
   The one part of Classica Codex that isn't offline. Two providers, side by side, since they trade off differently rather than one simply being better:

   Claude (Anthropic)
      Costs money - there's no free tier, though a single passage runs a small fraction of a cent. Doesn't train on what you send it. Needs its own developer account and API key, separate from a claude.ai login or a Claude Pro/Max subscription even if you already have one.

   Gemini (Google)
      Genuinely free - no payment method, no expiration, through Google AI Studio. The tradeoff: Google's free tier may use what you send it to improve their models, so this one isn't private the way Claude's API is. Worth knowing before choosing it for anything you'd rather not have looked at.

   Whichever you pick needs an internet connection - the only thing in this app that does. Nothing is sent until you click a button, and by default the app asks you to confirm every single time before it does. That confirmation can be turned off from AI Translation Settings once you're comfortable with it - it's a preference, not a one-time warning.

   Keys are stored encrypted, tied to your Windows user account, in a small file beside the database. Another account on the same machine can't read them.

Read Aloud
   In the same window, above AI Translation. Speaks the selected passage using whatever voices Windows already has installed - completely offline, nothing sent anywhere, no key needed. Greek is transliterated phonetically first, since no standard Windows voice can pronounce Greek script at all; Latin and English are read as-is.

   The voice list is Windows' own, from Settings > Time & Language > Speech. This app can't add voices Windows doesn't already have - install one there and it will appear here.

Create Translation (right-click a work in the library tree)
   Translates an entire work, not one passage, into a new translation edition saved permanently to your library. This is the answer to how much of the Renaissance and Post-Classical Greek collections have no English translation at all.

   Gemini only, since this is bulk optional content generation rather than core reading. A long work means many requests over several minutes; progress saves after every batch, so closing the window - or hitting the free tier's daily limit - never loses what's already done. Reopen it later and it picks up where it stopped.

   A part-finished translation is a normal thing to have, so the edition dropdown says so: an AI translation that doesn't yet cover its whole source reads "INCOMPLETE: 412 of 965 lines translated". Reopen Create Translation on that work to carry on. The note only appears on AI translations - a published translation divides the text differently from the original by choice, and counting its lines against the original's would say nothing useful.

Find Cross-Language Echo (right-click a line)
   Find Echoes, described under Analysis tools, works by shared rare words - which by definition can't connect a Greek original to an English translation of something else. This fills exactly that gap: a shared image or idea across languages, where the wording has nothing in common.

   It compares against one work you choose, not the whole library, and it uses an AI provider, so the same caveats as AI Translation apply. Treat what it finds as candidates worth reading, not conclusions.
"""),

        ("Searching", """
Click Search in the toolbar to open the search window. It stays open beside the reader rather than blocking it, so you can double-click a result, read it in context, and come straight back to the list.

Results are shown with the matched words highlighted. Double-click one to open it in the reader; right-click for Copy to Clipboard, or Export All Passages to write the whole result set to a document.

Match
   Anywhere in the line - the default. Matches the letters you typed wherever they appear, so "arm" also finds "arma" and "harm". It never silently misses anything, which is why it's the default.
   Whole words only - the word itself, so "arm" no longer finds "arma". With the word index built (Setup Wizard), this also ignores accents and final sigma, so a Greek word matches however the edition happens to accent it - type theos unaccented and it still finds the accented form. Without the index it still rejects substrings, but only finds the spelling you typed.
   All words, any order - every word you typed must appear somewhere in the line, not necessarily together. This is how to ask which passages mention two things at once.

Narrowing
   Language - Greek, Latin, English, or any combination.
   Text - originals, translations, or both. Not the same as language: an English original and an English translation of a Greek text are both "English" and are not the same thing.
   Author and Era - one author, or a broad period.
   Tagged and Bookmarked - search only inside your own tagged passages, or only ones you've bookmarked.

Clear Filters resets the narrowing without clearing what you typed.

Recent searches
   Every search you run is remembered, with its filters, and listed in the Recent box - most recent first, the last ten kept. There is nothing to save and nothing to tidy up.

   Pick one and it loads and runs straight away. Running the same search again moves it back to the top rather than listing it twice, so the list stays a record of what you have actually been doing.

   Each entry records the author by name and the era by its label, not by internal identifiers - so it still means the same thing after a corpus has been re-ingested, and if it names an author you no longer have loaded it simply finds nothing rather than quietly matching somebody else.

Era dates are the same rough consensus estimates the Timeline uses, which is why the periods are broad. An author counts as being in a period if their dates overlap it at all, so someone who straddles a boundary appears under both rather than falling between them.

Long searches stop at a limit rather than returning everything - a very common word can match tens of thousands of lines. When that happens the results say so plainly ("showing 500 of 5000+"), so a truncated list is never mistaken for a complete one. Narrow the search to see the rest.

A note on inflection: this search matches letters, not dictionary headwords - it does not expand a Greek verb into all its inflected forms. For that, use Word Study on a word in the reader, which does exactly that and lists every occurrence of the headword across the library.

Morphology (toolbar)
   Searches by grammatical form instead of by word - every aorist optative, every genitive absolute, every superlative adjective. Where Word Study answers "what is this word doing?", this answers "where else does the language do this?". Pick the categories you care about and leave the rest as Any.

   This is a Greek feature in practice. Greek lemma data carries positional grammatical tags that this search is built around; the Latin data uses a coarser, differently-shaped vocabulary, so most combinations will find nothing on the Latin side. The form says so on screen rather than just returning an empty list.
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

   Right-click a node to see related artifacts from the Art & Archaeology collection, if you've loaded it.

Bookmarks (toolbar)
   Everything you've bookmarked, newest first, with your note beneath each. Double-click one to jump to it.

   Bookmarks and tags are pinned to a passage's citation reference, not to a position in the file, so re-ingesting a corpus doesn't disturb them. If a text is ever removed or re-ingested with different citation references, any bookmark on a passage that isn't currently in the library goes quiet rather than being deleted - the window says how many are in that state, and they come back on their own if that text is loaded again.

Compare Sources
   From the Tag Browser, pick a tag and then two or more works it appears in, and read them in side-by-side columns - Hesiod, Aeschylus, and Ovid on Prometheus all at once.
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

Compare Translations
   Two or more translations of the same work in side-by-side columns - three English Agamemnons at once, each independently scrollable. This is the one to reach for when the question is how translators differ; Compare Sources (from the Tag Browser) is for how different authors treat the same subject.

Places Map
   A map of the ancient world built from your own place tags. Click a place to see every passage that mentions it. Tick "Show all known places" to see the places the app can locate even where you haven't tagged anything there yet.

   Real coastlines come from the optional World Map Data setup step; without it the map still works, just with rougher built-in shapes. If the Art & Archaeology collection is loaded, objects found at a place appear alongside its passages - descriptions are stored locally, while the photographs load from Perseus's own server as you view them, so that part needs a connection.
"""),

        ("Exporting passages", """
There are two exports, for two different shapes of thing.

A run of lines from one work
   Right-click any line in the reader and choose Export.

Scope
   A set number of lines from that point, everything to the end of the work, or the entire work from its beginning.

Options
   Show citation refs - the [1.1] markers, on or off
   Combine into one continuous passage - merge the lines into flowing text instead of one line per entry
   Include both original and translation - a bilingual document

Formats: plain text, Word (.docx), or PDF.

About bilingual export: the two editions are matched by citation reference, which is the only thing they genuinely share. Where a translation is divided more coarsely than the original - an English chapter covering a dozen Latin sections - it's matched to the section it covers and shown once. Introductions and cast lists that exist only in the translation are still included. The status line tells you how many lines actually paired.

A set of passages gathered from across the library
   Right-click anywhere in a results list and choose Export All Passages. Available wherever the app gathers passages from more than one work: the Tag Browser, Concordance, Word Study, Intertextual Echoes, Reception History, Auto-Tag, Morphology, Places Map, and Bookmarks.

   It exports the whole list, not the row you clicked - that's what the word "All" is doing there. A single passage already has its own export, from the reader.

Options
   Show citation refs - as above
   Show author and work - on by default here, since a set spanning twenty authors is unreadable without it
   Group by work, with headings - sorts by author and work and puts a heading over each. Turn it off to keep the list's own order, which for a concordance or an echo result is itself meaningful
   Include translations where available - pairs each passage with its counterpart edition, by citation ref, the same way bilingual export does. Passages whose work has no second edition loaded simply appear on their own; the status line says how many paired.

These documents always use a font that covers both Greek and Latin script, because a set gathered from across the library routinely contains both.
"""),

        ("Appearance and settings", """
Dark Mode / Light Mode
   Toggles from the toolbar. Dark mode avoids pure black on pure white deliberately - maximum contrast is harder to read for long stretches, which rather defeats the point. Your choice is remembered.

Database Location
   Where the library, tags, and bookmarks are stored. Set it as the first step of Guided Setup, or from Advanced Setup's Database Location button - either one changes the same setting. Point it at a different file to keep entirely separate libraries. You're only asked for this on a first run, or if the file has been moved or deleted.
"""),

        ("Where your data lives", """
The database is a single SQLite file. By default:

   %LocalAppData%\ClassicaCodex\classicacodex.db

It holds everything you create - tags, bookmarks - alongside the ingested texts. Copying that one file backs up the lot, and it can be moved anywhere you like from Setup Wizard - the Database step in Guided Setup, or the Database Location button in Advanced Setup.

Downloaded source repositories default to:

   Documents\ClassicaCodexData\

Those are just working copies of public data. Deleting them costs nothing but the time to fetch them again; your tags and bookmarks live in the database, not in those folders.

Preferences (theme, category shapes, database location) sit in small files beside the database.

None of the texts, dictionaries, or linguistic data belong to this app - see About for the full attribution and licensing.
"""),

        ("When something looks wrong", """
A few things the app reports about itself, and what they actually mean.

"Out of date - N of M lines indexed"
   In Setup Wizard, under Tools, open Word Index. The word index is built from the texts, and nothing rebuilds it automatically when a new corpus is ingested afterward - so this is telling you a source was added since the last build and its lines won't turn up in lemma-aware search yet. Rebuilding is safe at any time and always starts from scratch.

"N file(s) were skipped"
   After a setup step. A corpus is tens of thousands of files and a few failing to parse is ordinary - the rest ingested normally. What it means concretely is that the works in those particular files won't be in your library. The first several are listed, and the full list goes to:

      %LocalAppData%\ClassicaCodex\ingest-skipped.log

Something went wrong with that action
   An error that wasn't anticipated anywhere more specific. Your library, tags, and bookmarks aren't touched by these - the app keeps running and the rest of the session is fine. Details go to:

      %LocalAppData%\ClassicaCodex\errors.log

Search finds nothing for a word you can see on screen
   Usually the lemma data for that language hasn't been loaded, or the word index hasn't been built since that text was ingested. Both are Setup Wizard steps. Failing that, try the word exactly as it appears - accents and breathings are ignored when matching, but a word split across a line break isn't one word as far as the text is concerned.

A translation pane that says the edition has no text
   The edition was catalogued but its source file didn't parse. Re-running that corpus's setup step will retry it.
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
