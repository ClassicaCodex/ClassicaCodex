namespace ClassicaCodex.UI;

/// <summary>
/// A plain topic-and-text help window. Deliberately describes what the app
/// actually does - including the places where results are approximate - so a
/// new user isn't left guessing whether something is broken or simply
/// working as well as the source data allows.
/// </summary>
public class HelpForm : ScaledForm
{
    private readonly ListBox _topicList;
    private readonly TextBox _contentBox;

    private static readonly (string Title, string Body)[] Topics =
    {
        ("Getting started", """
Classica Codex reads the Perseus Digital Library - the Greek and Latin classics, their English translations, dictionaries, and the linguistic data that makes searching them work properly - plus several optional collections that extend it further:

   Post-Classical Greek - Greek writing from after the classical period into late antiquity.
   Latin Church Fathers (CSEL) - the critical editions of Augustine, Ambrose, Jerome, Cyprian and their contemporaries, from the volumes out of copyright.
   Patrologia Latina - Migne's collection, Tertullian to the twelfth century, and much the largest thing here. A 19th-century reprint rather than a critical edition: where a work appears in both, CSEL is the text scholars cite and this is the wider net. Both can sit side by side, and the same work simply gains a second edition in the dropdown.
   Renaissance English - Shakespeare and the other writers who reworked classical material in English.
   Political Theory - Bodin's Six Books of the Commonwealth in French, Latin and English. One work rather than a corpus, and worth having because the French of 1577 and the Latin of 1586 are both Bodin: putting his own two versions side by side in the reader is a comparison you cannot usually make.
   Medieval Nordic manuscripts - see Manuscripts and editorial notes.

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

   Which of them a work opens on is yours to set. "Open works from:" under Reading in the Setup Wizard picks a collection, and any work carried by more than one opens on that collection's edition - Perseus and First1KGreek both have the Agamemnon, CSEL and Patrologia Latina share a good deal of Augustine. It is a preference, not a filter: the other editions stay in the dropdown in the same order, and only which one is already selected changes. Works that collection doesn't have open exactly as before, so a preference set once needs no maintenance as the library grows.

Collate Editions
   Where a work is held by two collections, the Collate button on the toolbar shows what the two editions disagree about. It is the one thing here that needs more than one collection to exist at all: a single corpus gives you one printing of a text and no way to know what it settled.

   Comparing two editions character by character reports almost every line as different, which is worse than no comparison because it looks like evidence. So differences are graded, and each passage is filed under the first kind that explains it:

      punctuation - spacing, case, brackets, the elision mark. Editorial brackets count here on purpose: one editor bracketing a word another prints plainly disagrees about the word's standing, not about whether the word is there.
      spelling - Greek accents and breathings, final sigma, Latin u/v and i/j, the ae digraph however it is written. Conventions editors differ on without changing which word is printed.
      line division - the same words broken across two lines at a different point, usually one edition hyphenating a word at a line end.
      THE WORDS - the editions print something different. This is the only one that is a reading, and the view opens on it.

   The counts above the list say whether a pairing is worth reading before you read any of it. On the Aeschylus pairings in a full library, roughly a fifth of shared lines differ in the words and the rest is typography.

   A work with more than two editions offers every pairing of them, and two editions from the same collection pair like any other — Perseus alone carries two editions of Ajax and of a dozen Plutarch works, which are two independent printings of one text and exactly what this is for. Where the collection names do not tell two pairings apart, the CTS version identifier is added.

   Some pairs cannot be collated, and the window says so rather than inventing a result. Two editions that divide a work differently can still collide on plain numbers - both label their passages 1, 2, 3 - so they look aligned and then disagree at every one. Where that happens the collation is refused with the reason.

   Export... writes what you are looking at to CSV, tab-separated text, or Excel — and right-clicking the list does the same, plus copying rows to the clipboard. The file carries the filter you had applied, both editions with their CTS identifiers, the counts, and any warning shown above the table. That matters more here than in most exports: four columns of Greek with no note of which two editions produced them, or that 89% of the lines differed, is a file that will be misread later.

   Nothing here says which reading is right. That is not a question the app can answer and it does not pretend to.

Scrolling and selection are synced between the panes. That works best on verse, where a translation keeps roughly the same line structure as the original. Prose translations often carry far fewer citation points than the original does, so the two sides drift apart; that's how the texts were digitized, not a fault in the alignment. The ⇅ button on the toolbar turns that off: linked panes suit verse, where a translation keeps roughly the original's line structure, but on prose one Greek sentence can become three English ones and the two sides drift apart until the mirroring is dragging you away from the line you were reading. Your choice is remembered.

Hover any line to see its citation reference.

Marks at the end of a line
   ?  an inquiry has been started from this passage
   #  it carries at least one tag
   ★  it is bookmarked

   All three appear together, in that order, on a line that has all three. A line tagged five times still shows one #: the mark says there is something recorded here, not how much.

   They are drawn, not stored - copying, exporting or searching a line gives you the text and nothing else, the same way an athetized line is shown in italic rather than by putting brackets into the words. Three plain characters rather than icons because the reading panes use whatever font you have chosen, and an ornamental glyph would arrive as an empty box in exactly the Greek and medieval faces this app exists to display.

   They are per edition, not per work. A passage bookmarked in the Greek does not show a star opposite it in the translation, because that is a different passage with its own annotations - even though both sides carry the same citation reference. And because tags and bookmarks are recorded against that reference rather than an internal id, the marks come back after a corpus is re-ingested, along with the annotations they stand for.

   Marks added from the right-click menu appear at once. Deleting a tag or bookmark from the Tags or Bookmarks window clears its mark when that window closes.

Right-click a line for:
   Copy to Clipboard - the line's text on its own
   Start inquiry from this passage - begin with your own observation and a small question
   Tag this line - file it under a name you choose
   Bookmark this line - save it with a note
   Find Echoes - look for passages elsewhere that share rare wording
   Find Cross-Language Echo - the same idea across languages (see Translate)
   Reception History - the same, split into earlier and later authors
   Translate - look up, generate, or listen to an English rendering of the line
   Word Study - look the words up properly
   Export - write the passage out to a file

The library tree lists every author alphabetically. The box beside the Library button filters it by author name as you type - with a few thousand authors loaded, typing three letters beats scrolling. Clearing the box brings them all back.

Showing one collection at a time
   The funnel icon beside that box opens a list of the collections you have installed. Tick one or more and the tree narrows to them; leave them all unticked and you see everything. Hovering the icon says which state it is in, since a filter you have forgotten you set looks exactly like a library that has lost half its contents.

   Works are filtered as well as authors, so an author only appears while one of their works does. That matters where an author is in more than one collection: Ambrose has works in both the Church Fathers and the Patrologia Latina, and narrowing to one shows him with just that collection's works beneath him rather than all of them.

   The list is built from what is actually in your library rather than from what could have been installed, so it is right even if you have since deleted the downloaded files. It only appears once you have more than one collection - with a single one there is nothing to choose between.

Favourites
   Right-click a work and choose Add to Favourites to mark it with a star. The star checkbox on the filter row then narrows the tree to favourites only, and authors with nothing favourited drop out rather than showing empty.

   A shortlist of the dozen texts you actually return to, out of a corpus of several thousand. Favourites are stored against the work's CTS URN rather than an internal number, so they survive a corpus being re-ingested - the same way tags, bookmarks and your reading position do.

Right-click a work in the library tree on the left for:
   View Details - everything known about the work and its editions
   Translate This Myself - the workbench, one passage at a time
   Create Translation - renders a whole work at once rather than a single line, see Translate
   Core Vocabulary - which words the work is made of, ranked by how much of it they account for
   Add to Favourites - mark it with a star, and filter the tree to favourites

View Details
   The catalogue entry for the work and each edition of it you have loaded - CTS URNs, language, translator, line counts - and, read straight from each edition's source file, the publication details its TEI header carries: who edited it, which printed edition it was digitised from, the publisher and year, and the licence.

   That printed source line is usually the one worth having. "Homer, Homeri Opera, David B. Monro, Thomas W. Allen, Oxford, Clarendon Press, 1920" tells you exactly which text you're reading, which matters before quoting it anywhere.

   This is read from the file each time you ask rather than stored in the library, so it needs the corpus files still to be where setup put them. If you've since deleted them the reader carries on working perfectly and this view simply says the header isn't available for that edition.

Back and Forward
   The two arrow buttons at the left of the toolbar retrace where you have been. Ten things in this app end in "jump to it" - a search result, a concordance line, an echo, a place on the map, a figure in the myth network - and following one used to cost you the passage you were reading. Alt+Left and Alt+Right do the same, as in a browser.

   This is a record of the current session only, and is not kept when the app closes. Where you left off, which is remembered, is a separate thing.

Keyboard shortcuts
   Escape closes any window you are looking at - a results list, a map, a comparison. It deliberately does nothing in the setup wizards or while a corpus is being ingested, where it would abandon work halfway rather than dismiss a view.

   In the reader: Ctrl+F opens Search, Ctrl+L jumps to the author filter (opening the library panel first if it is collapsed), F1 opens this help, and Alt+Left and Alt+Right walk back and forward through the passages you have jumped to - the same keys a browser uses, for the same reason.

   In the translation workbench: Ctrl+Enter saves the passage and moves to the next one, Ctrl+S saves without moving, Ctrl+G opens the Go to list, and Alt+Left and Alt+Right move a passage at a time. None of these are unmodified keys, because Enter has to keep making a new line in your translation and Escape has to stay available to the text box rather than discarding what you have half written.

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
   An optional online-assisted part of Classica Codex. Two providers, side by side, since they trade off differently rather than one simply being better:

   Claude (Anthropic)
      Costs money - there's no free tier, though a single passage runs a small fraction of a cent. Doesn't train on what you send it. Needs its own developer account and API key, separate from a claude.ai login or a Claude Pro/Max subscription even if you already have one.

   Gemini (Google)
      Genuinely free - no payment method, no expiration, through Google AI Studio. The tradeoff: Google's free tier may use what you send it to improve their models, so this one isn't private the way Claude's API is. Worth knowing before choosing it for anything you'd rather not have looked at.

   Whichever you pick needs an internet connection. Nothing is sent until you click a button, and by default the app asks you to confirm every single time before it does. That confirmation also covers Research Bench Gemini calls and can be turned off from AI Translation Settings once you're comfortable with it - it's a preference, not a one-time warning. Crossref publication discovery and live artifact images are separate online lookups described in Research Bench and Places Map below.

   Keys are stored encrypted, tied to your Windows user account, in a small file beside the database. Another account on the same machine can't read them.

Read Aloud
   In the same window, above AI Translation. Speaks the selected passage using whatever voices Windows already has installed - completely offline, nothing sent anywhere, no key needed. Greek is transliterated phonetically first, since no standard Windows voice can pronounce Greek script at all; Latin and English are read as-is.

   The voice list is Windows' own, from Settings > Time & Language > Speech. This app can't add voices Windows doesn't already have - install one there and it will appear here.

Core Vocabulary (right-click a work)
   Every headword in the work, ranked by how many of its running words that headword accounts for, with a running total beside it. The line at the top is the useful one: learn the top so-many headwords and you can read half the work, so-many more and you reach four fifths.

   This is the standard apparatus of learning a classical language, and it is the thing to look at before starting a text rather than after. A work is rarely hard because its grammar is exotic; it is hard because every third word is unknown, and a few hundred headwords carry most of any Greek or Latin author.

   Counted from the text itself rather than from the word index. The index records each word once per line - it answers which lines contain a word, not how many times - so a frequency list built on it would be reporting line counts as word counts. That also means this is unaffected by the word index being out of date.

   Where a form could belong to more than one headword, its occurrences count towards every candidate rather than being split or assigned to a guess, and those rows say so. The running total still counts each word of the text once, so it stays a true share and cannot climb past everything. Forms with no lemma data at all are counted in the total but can never be covered by the list, and the figure at the bottom says what share of the work those are - on a work with thin lemma data the running total stops well short of everything, which is the honest answer rather than a flattering one.

Where should I start? (toolbar)
   The scroll-and-quill button on the right of the toolbar. A short list of works worth translating first, ordered roughly as they are taught, with a sentence on why each one. Only works actually in your library are shown. Picking one and clicking Translate This opens it straight in the workbench.

   It also names the ones to save for later - Aeschylus, Pindar, Sophocles and Thucydides in Greek, Tacitus, Persius and Lucretius in Latin. Greek choral lyric and Latin satire are genuinely difficult for professional scholars and the text itself is often uncertain. Starting there tells you nothing about whether you can learn the language, and nothing else in the app gives any sign that one text is ten times harder than another.

Translate This Myself (right-click a work in the library tree)
   A workbench for translating a text yourself, one passage at a time. It shows the passage, a list of its words, and your own translation box side by side.

   Whole Passage lays out every word of the passage in order with its dictionary headword and its grammatical form, so you can see how the sentence is put together and write a first attempt of your own rather than jumping straight to the AI button. Where a form could be more than one word - common in Greek - it uses the words around it to decide. A masculine accusative article in front of a form rules out a preposition; an adjective takes its gender and number from the noun beside it. That settles most of what a word cannot settle alone. Where nothing agrees, the alternatives are listed rather than one being chosen for you.

   The neighbours narrow the possibilities rather than choosing among them, and what is shown is what all the surviving readings agree on. So context adds detail where it genuinely settles something, and stays quiet where it doesn't.

   It also claims only what a form actually determines. The genitive plural article is the same word in all three genders, and every neuter nominative is also an accusative, so those forms are shown as "article: genitive plural" and "adjective: neuter plural" rather than picking a gender or case the word itself cannot tell you.

   It shows no dictionary meanings, on purpose. LSJ and Lewis & Short mix definitions with manuscript notes and grammatical cross-references, and no reliable way was found to tell them apart automatically - a wrong meaning presented to someone still learning is worse than none. Click a word on the left for its full entry, where an apparatus note is at least visible as one.

   Word Study opens the passage in the full word study window with whichever word you had selected already chosen, and its occurrences narrowed to the work you are translating - how this author uses the word here, rather than a corpus-wide count that stops at the result limit. Choose Texts widens or narrows that to any set of works you like.

   Alphabet opens the letters of whatever language you are translating from, with the breathings, accents and iota subscript that sit on them. It is not a translation aid - it is for the step before, when you cannot yet read the script well enough to look a word up at all.

   Your own translations of the passage before and after are shown greyed above and below the box you type in, read-only so they can't be edited by accident. They are there for consistency as much as context: what goes wrong in a translation built over weeks is usually rendering the same word one way here and another way later, and this is the only place you would notice.

   The passage before and after the current one are shown dimmed around it. Citation references cut across sentences all the time in verse - a relative pronoun at the end of one passage often resolves in the next - so translating a clause with half of it off screen is how you end up confidently wrong.

   Go to jumps straight to any passage, and doubles as the progress view: every passage is listed with its citation reference, a tick once you have translated it, and enough text to recognise it by.

   The word list is every word of the passage in the order it appears, repeats included - it is meant to be walked alongside the sentence, so it has to match the sentence.

   Click any word to see its dictionary headword, its grammatical parse - case, tense, mood, voice - and the LSJ or Lewis and Short entry. All of it is looked up from the lemma and lexicon data you have loaded, not generated, so it is the same information a printed commentary would give.

   AI Translate Passage asks Gemini for a rendering of the whole passage. AI Translate Word asks what the selected word means in this particular line - which sense is in play and what it is doing in the sentence, the part a lexicon entry can't tell you. Both appear in the reference panel, never in your box: an answer that arrives already typed stops being something to weigh and becomes the answer. Both send text over the internet and are labelled as AI-generated where they appear.

   Check against picks which existing translation to measure yourself against, when the work has more than one. Nothing in the data records which original a translation was made from, so the app infers it from shared citation references and marks its best guess as "lines up with this text" - a default to change, not a decision made for you.

   If the work has two or more original-language editions, you are asked once which you are translating from. That choice decides the citation references every passage is filed under, so it can't be changed later without orphaning work already done.

   Compare published shows the chosen translation of the same passage, and stays disabled until you have written something - the button says which of the two reasons applies. Reading someone else's rendering first is the one thing that quietly removes the point of doing this at all.

   Each passage saves as you move on, so a work can be picked at over months. Reopening starts at the first passage you haven't done. Your translation is named when you begin and appears as its own edition everywhere - the reader, Compare Translations, search, and export.

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
   Collections - which of the downloaded collections to look in. Tick any number of them; leave them all unticked to search everything. This is a different question from Language: the Church Fathers and the classical Latin texts are both Latin, and "search only the Church Fathers" cannot be asked any other way. The button says how many are selected, and only appears once you have more than one collection.

Clear Filters resets the narrowing without clearing what you typed.

Every passage, or one row per document
   The Show box beside the results switches between the two. By passage is the default and lists every matching line. By document lists each work once with the number of matches in it - which is the better question when you want to know where a word is concentrated rather than read each occurrence.

   Double-click a document to list its matches; switch Show back to see every document again. Both views come from the same search, so changing between them costs nothing and finds nothing new.

   One caution: searches stop at a limit, and when that happens the counts cover the matches that came back rather than everything in the library. The status line says so when it applies. A document count is a good guide to where to look and not a census.

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

   Choose Texts narrows the search to any set of works, the same picker Word Study uses. It matters more here: a pattern like every aorist optative matches tens of thousands of lines, the search stops at its result limit in author order, and what comes back is therefore the start of the alphabet rather than a sample of the corpus. Narrowing the scope is the only way to ask the question about a text you actually care about.

   In that picker, the Show box switches between all texts, the ones you have chosen, and the ones you have not. Picking through a couple of thousand works takes several rounds of filtering, and by the end there is no other way to see what you have accumulated - every choice you made is off screen the moment the filter moves on.
"""),

        ("Tags and the Myth Network", """
A tag is any label you attach to lines - a god, a hero, a place, a theme. Tag lines by hand from the reader's right-click menu, or in bulk:

Auto-Tag (inside Myth Network)
   Type a name, add any alternate spellings translators might use (Athene, Pallas, Minerva), and search the whole corpus at once. Every match is listed with its passage so you can uncheck anything wrong before committing - a name search on a proper noun does over-match sometimes, and won't catch a figure referred to only by epithet.

The Myth Network
   Tags become nodes; two tags are linked when they occur together. Circle size is how often you've used a tag, line thickness how strongly two co-occur.

   A tag naming an Olympian, a king, a hero, a place or an object gets a portrait inside its node - sixty are included. The match is on the tag's own text, so a tag called Zeus finds Zeus; anything else keeps the plain coloured node. Portraits appear only on nodes big enough to show one, which means tags you've used half a dozen times or more - below that a face is a smudge, and those are the tags you least need to pick out anyway. The category shape stays as a ring around the portrait, so it still tells you gods from kings.

   To add your own or replace one, put a PNG in a "Figures" folder beside your database file, named for the tag. Those take precedence over the ones shipped with the app.

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

Occurrences start scoped to the work you are reading, because the corpus-wide count for a common word runs to thousands of lines, stops at the result limit, and tells you only that the word is common. Choose Texts picks any set of works instead - one text, a trilogy, an author's whole output, or everything. The list is filterable, since a full corpus runs to thousands of works. Right-click it to select or unselect everything the filter is currently showing, which is the quick way to take an author's whole output: type their name, right-click, select all shown.

You'll get the dictionary headwords that form could come from (sometimes more than one - that ambiguity is real and worth seeing rather than having it guessed away), the full paradigm of attested forms, the dictionary entry itself, and every occurrence across the corpus.

Dictionary entries come from Liddell-Scott-Jones for Greek and Lewis & Short for Latin. Load them from Setup Wizard if you haven't.
"""),

        ("Analysis tools", """
Concordance
   A keyword-in-context view: every occurrence of a word lined up with what comes before and after, so patterns of usage are visible at a glance.

Stylometry
   Compares writing style using Burrows's Delta, a standard authorship-attribution measure based on how often each author reaches for common function words. Runs on original-language texts, since it's comparing the actual words an author wrote.

   Delta measures how similar two texts are in their word-frequency profile. That is not the same as measuring who wrote them, and on a corpus where everything is the same genre the two come apart more than you might expect. The controls are there to let you find out whether a result is about the text or about the settings.

   How text is counted
      Fold accents merges differently-accented forms of the same letters. It removes inconsistent accentuation between editions, at the cost of merging genuinely distinct words. Most frequent words sets how many features Delta compares on; Burrows used 150, and anything much below 100 gets unstable.

   Which works to compare against
      Skip fragment collections and indices keeps out things that are not single compositions - a Fragmenta is an anthology assembled by a modern editor out of quotations spanning centuries, and an index is a word list. Neither has an authorial style, and both distort what normal looks like for everything else.

      Minimum length drops works too short to measure reliably. Below roughly 2,500 tokens, word frequencies are noisy enough that a text's Delta says more about its length than its author.

      Sample size splits every work into equal-size random word samples, so nothing is compared against a text much longer or shorter than itself. Without it, longer texts systematically look more like everything - this is the single largest confound in the method. Remainder words are discarded, which is reported after a run.

   Save run and Run whole author
      A single Delta figure is close to meaningless alone; the useful question is always comparative. Run whole author computes and saves every work by one author at the current settings, which gives you a reference distribution to read an individual result against. Compare Runs then shows where each work sits in it.

   Reading the results honestly
      Vary a setting and re-run. If a work's position moves, the position was about the setting.

      Check the length confound before believing a ranking. If a measure correlates with token count, the ranking is measuring how much text there is. Four separate measures turned out to be doing exactly that, so the Validation window now computes the correlation on every run rather than leaving it on a tab someone has to remember to open.

      Depth to first outsider - how far down a work's neighbour list you get before another author appears - is shown because watching it move is instructive, but it is not reliable for attribution. In testing it varied by up to 20 ranks for a single work on a 500-token change in sample size. Delta floor, the distance to the nearest neighbour, is more stable.

      Replicate at more than one sample size before believing anything. In the work this feature was built for, the single most promising result turned out to be an artifact of which words happened to fall into one sample.

The validation bench
   Three windows that exist to attack a result rather than produce one. They open from each other: Validate settings on the Stylometry window, then Test parameter stability and Perturbation series from there.

   Validation - can these settings recover known texts?
      Hides one work at a time and asks whether the method puts it back with its own author. Reports margin - mean Delta to other authors minus mean Delta to its own - along with the correlation between margin and text length, which is the thing to read first. On a same-genre pool recovery saturates at 100% and tells you nothing; the correlation is what says whether a ranking is about style or about size.

   Stability - does the result survive a change of settings?
      Runs the validation across a grid of sample sizes, feature counts and accent settings. Every cell carries a 95% band, because on a corpus of twenty works the spread across forty configurations can sit entirely inside the estimation error of any one of them. If the bands overlap, the ordering is noise and the summary says so rather than naming a best cell.

   Perturbation - how much disturbance before the method stops recognising it?
      Replaces a chosen percentage of a work's words with another author's and measures what happens to the margin. Replace mode holds the token count constant, so length cannot move while composition does.

      Always run the same-author control, which is on by default. A falling curve alone cannot tell disturbance by another author's style from disturbance as such - injecting a work's OWN author moves the margin the other way, and the two curves diverging is the evidence. If the control falls too, something is wrong with the experiment rather than interesting about the text.

      Run every work by this author sweeps the whole author and reports two things a single series cannot. The first is whether any work responds unusually once the part of its response that is predictable from its baseline margin has been fitted out - works with more margin to lose lose more of it, so a ranking on the raw drop mostly rediscovers which work has the least. The second is detection power: how large a contamination this method could find on this corpus at all, as the probability of correctly ranking a contaminated work above a clean one. Read that before believing a null result. On the Greek tragic corpus nothing below about a third of a play is reliably detectable, which bounds what any negative finding there can mean.

      Save experiment stores a run in the library with its seed, its exact pool and every setting, so it can be reloaded and re-run. Load puts all of that back on the controls; if the pool has changed since, the status line says so, because a margin is a property of the comparison rather than of the text and is not comparable across pools.

   Right-click any results table to copy it or export to CSV, tab-separated text, or Excel. The export carries full precision rather than the rounded figures on screen, plus the seed and settings as a header, so the numbers cannot be separated from what produced them.

   docs/stylometry-notes.md in the repository is the longer write-up: what the tool found, what it got wrong, and why the authorship question it was built for came back unanswered. Six candidate findings appear there; five of them dissolved on checking, and how each one dissolved is the most useful part.

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

   The catalog holds two hundred places, sorted into cities, sanctuaries, battlefields, regions and islands, and rivers and seas. Each has its own pin colour, and the row of toggles above the map switches a kind off - two hundred names at Mediterranean scale is more than can be read at once, and most of the time you want one sort of thing. Names are drawn only where they fit, in order of how often you have tagged the place, so the ones you use win the space and more appear as you zoom in. Every pin is always drawn, whether or not it has room for a name.

   The categories are the reason you would look a place up rather than a gazetteer's ontology, and a few are arguable: Salamis is an island and a battle, Rhodes an island and a city on it, Delphi a sanctuary and a polis. Each takes the sense a classical text most often points at. Nothing is ever hidden from the map by that choice - only its colour and which toggle hides it.

   Real coastlines come from the optional World Map Data setup step; without it the map still works, just with rougher built-in shapes. If the Art & Archaeology collection is loaded, objects found at a place appear alongside its passages - descriptions are stored locally, while the photographs load from Perseus's own server as you view them, so that part needs a connection.
"""),

        ("Research Bench", """
Research Bench turns a work you have been reading or translating into a durable research workspace. Right-click the work in the library tree and choose Research. Everything in the bench belongs to that work, while a work may have several independent projects.

The basic structure
   A project is the broad inquiry or working theory. Give it a useful name, record its scope in the project notes, and keep its status current rather than replacing it when your judgment changes.

   Status is active, on hold, concluded, or archived, and it takes effect as soon as you choose it. Archiving is not deletion: the questions, evidence and log are all kept, the Archive button becomes Restore, and the Show list above the project list decides which statuses you are looking at - Current, meaning everything except archived, being the default. A project opened directly from a passage inquiry appears whatever its status, so following a note never leads to an empty window.

   Questions break the project into things that can actually be answered. Evidence may be linked to one question or kept at project level. Reorder questions to match the investigation rather than the order in which they occurred to you.

   Evidence records what you have, where it came from, and what you currently think it does. Source text, provenance, canonical reference, stable identifier, relationship, review judgment, and your interpretation are separate fields deliberately. A quotation is not an argument, and an AI interpretation is not your judgment.

Starting from one passage
   Right-click a passage in either reader pane and choose Start inquiry from this passage. The first screen stays deliberately small: the excerpt and citation are fixed in view, while you write what caught your attention and draft a question in your own words.

   Read closely, Compare, and Research are directions rather than conclusions. Read closely keeps attention on language and form; Compare suggests placing the passage beside another text, translation, genre, or reception; Research opens a larger path. The note is saved by the edition's CTS identity and citation, so it survives a corpus re-ingest.

   AI appears only after you choose Research. If requested, Gemini receives this one passage, its author/work/citation, and the two notes visible in the form - not the rest of the corpus, the Research Bench, or the database. Suggestions remain outside your draft until you select one, and anything copied into the question box stays editable.

   Turn this into a Research Bench project appears only after your observation and question have been saved. Promotion creates a normal project, an initial research question, and a manual primary-text evidence record carrying the excerpt, CTS references, and your note. The original inquiry remains linked to the project so reopening the passage can take you back to it.

Starting without a question
   Let AI Suggest a New Project is for the point where a work interests you but you do not yet know what to ask about it. It combines the work's attribution record, existing project titles, a bounded sample of its locally ingested original-language text, and optional Crossref publication metadata. Gemini proposes several established-debate, corpus-question, or explicitly novel-theory blueprints.

   Inspect a proposal before creating it. Each blueprint shows a central question, rival hypotheses, planned experiments, falsification criteria, locally keyed passages, and publications to investigate. Choosing one creates a normal project that you can rewrite or reject; it does not create conclusions or pretend that a suggested analysis has already run.

   Crossref receives only the scholarly search terms shown at the top of the window. It returns bibliographic metadata and, where a publisher deposited one, an abstract. Those publications enter the Reading Queue as unreviewed leads. A title or DOI is never treated as evidence of what its author argues. If Crossref is unavailable, project discovery continues from the local corpus alone.

   Gemini receives the context named in the confirmation dialog: project titles, attribution information, retrieved metadata, and a bounded corpus sample with opaque passage keys. Returned publication and passage keys are checked locally; invented keys are discarded. Prompt provenance and a fingerprint of the corpus sample are stored with accepted AI hypotheses and experiments.

Gathering evidence
   New evidence creates a manual record. Use a stable CTS or DOI identity where possible and record enough provenance that you could find the material again after re-ingesting the corpus or moving a source file.

   Attach saved stylometry run connects a real saved result to the project. Open this saved run in Stylometry returns to the normal analysis window with that run loaded, so the evidence can be checked against its pool and settings.

   AI: Find relevant corpus passages and AI: Challenge the working theory send the project context and a bounded original-language edition to Gemini. Returned citations must resolve against that exact local edition before anything is offered to you.

   What comes back is shown for review before it is kept: each candidate with its reference, relationship, the model's confidence and rationale, and the local corpus text the citation resolved to. Tick the ones worth having and only those are saved; Discard all writes nothing at all, not to the evidence register and not to the research log. What you keep is stored as an uncertain AI candidate carrying the local passage text, model, prompt, corpus fingerprint and generated time - never as accepted evidence, and never without your having said so.

   The Project audit points out missing references, unreviewed candidates, unsupported findings, and other traceability gaps. It checks the state of your record; it does not decide whether an argument is true.

Reading Queue and source work
   Project > Reading queue & passage notebook is upstream of evidence. Add a corpus passage, queue an existing source, or create an external reading. Record why it matters, the exact quotation or passage, and your reading notes in their separate boxes. Mark it Reviewed only after reading it, then Promote to evidence when it genuinely belongs in the argument.

   Project > Import RIS / BibTeX bibliography imports structured citations without flattening their fields. Bibliography & Zotero export writes RIS or BibTeX for another reference manager; the Zotero route requires Zotero's local integration to be available.

   Source files & page notes attaches a local PDF to evidence without putting the PDF inside the database. Classica Codex records its path, size, modification time, and SHA-256 fingerprint so replacement or disappearance is visible. Page annotations retain the page number, exact quotation, note, and review judgment.

   The Scholarly claims matrix records propositions attributed to scholars separately from the publications themselves. Name the claimant, transcribe or summarize the claim responsibly, provide an exact locator, link its source, and record your verification and stance. An imported citation does not become a scholarly claim automatically.

Testing explanations
   Project > Hypothesis Lab keeps rival explanations side by side. A hypothesis needs a testable statement, not merely a topic. The assessment matrix records whether each reviewed source supports, contradicts, contextualizes, or fails to discriminate between the alternatives, with strength and researcher notes kept explicit.

   Falsification experiments state the expected outcome and what result would count against the linked explanation before running anything. Open method tool sends a planned Stylometry, Corpus Investigator, Parallel Studio, Bibliography, or Reading Queue experiment to the appropriate normal workflow. Changing an experiment to Completed is a human action; an AI suggestion never marks itself complete.

   AI challenge asks Gemini for rival hypotheses and discriminating tests. Check only the proposals worth retaining. Accepted proposals preserve their AI origin but remain candidates, not facts.

Echoes and close reading
   Saved same-language and cross-language echo searches appear under Echo investigations. Each investigation retains the source passage, search method, settings, target passages, scores, AI provenance where applicable, and your pending, accepted, or rejected disposition.

   Parallel Passage Studio opens a saved pair for close reading. Classify the kind of connection, possible direction, motifs, and your own parallel note. AI may suggest shared features, differences, lexical observations, alternative explanations, and verification tasks, but its reading is stored separately from your classification.

   Intertextual Atlas visualizes reviewed passage relationships as a network. Its lines aggregate saved passage-level records; click a node or edge to inspect the underlying pairs rather than treating the picture as independent evidence.

   Corpus Investigator begins with a reviewed parallel and asks Gemini to find locally resolved passages that might confirm, complicate, or falsify it across a chosen corpus scope. Candidates stay pending until you inspect them.

Findings, synthesis, and export
   Project > Synthesis & findings is where evidence becomes a proposition. Link only evidence you have weighed and state its role for that particular finding. Project-level and finding-level relationships may differ because one source can matter in different ways to different claims.

   AI synthesis drafts a labelled candidate from the saved record. It is stored beside, never in place of, the researcher conclusion. Rewrite or reject it; changing a finding's status is always a human judgment.

   Export the research dossier to Markdown when you need a portable record. It includes questions, evidence and provenance, bibliography, claims, findings, hypotheses, experiments, echoes, and unresolved audit concerns so an argument does not become separated from how it was produced.

Reproducibility and history
   Corpus snapshots freeze the identities, editions, attribution judgments, counts, and ordered-text hashes behind a project. Compare a later snapshot before treating two runs as directly comparable.

   Research log is an append-only account of project changes. Removing a question or evidence item does not erase the human-readable history of what happened. Archiving a project is recoverable and is the normal way to put finished or abandoned work aside.

The governing rule throughout the bench is simple: AI and automated searches may propose where to look, but the human researcher decides what was read, what was verified, what counts as evidence, and what the evidence warrants.
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

Every exported translation carries the edition it came from - "trans. Samuel Butler", "trans. Gemini (AI-generated)", or your own name if it is your workbench translation. Exported text outlives the application that produced it, and whether a rendering is a published translation, your own, or a machine's has to travel with it: that is the point at which text stops being something you are reading and becomes something you are pasting into an essay.

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
Text size
   The toolbar's Aa button sets how large Greek, Latin and English are drawn, in the reader and the translation workbench alike. Polytonic Greek is the reason it exists: breathings, accents and iota subscript are what you need to see in order to look a word up at all, and at a small size on a high-resolution display they are a few pixels each.

   The two sizes are linked by default, because text at two different sizes in adjacent panes reads as a mistake rather than a setting. Unlink them if you want the Greek larger while keeping more English on screen. The sample text in the dialog updates as you drag, and Cancel puts back what you started with.

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

        ("Manuscripts and editorial notes", """
Most of the library is printed editions - a text an editor settled long ago, with the manuscript evidence behind it out of sight. The Medieval Nordic material is different: those are transcriptions of particular manuscripts, made word by word from the parchment, and they carry the editor's working notes with them.

Reading levels
   Menota transcribes at up to three levels. Facsimile follows the page letter for letter, abbreviation marks and all. Diplomatic expands the abbreviations but keeps the scribe's own spelling. Normalised regularises spelling to a standard Old Norse orthography.

   The app reads one level per manuscript, chosen for whichever one covers nearly every word, and records it with the edition. Mixing them would produce a text belonging to no scribe and no dictionary, and nothing downstream could tell.

   This matters for Stylometry above all. Comparing a diplomatic text with a normalised one measures the scribes' spelling habits, not the authors' style - so the Menota survey reports each manuscript's level before anything is imported, and says which are safe to compare.

Editor's Notes
   The apparatus. Two kinds of entry appear here:

   A manuscript variant is a reading from another manuscript - the adopted reading, the alternative, and the witness it comes from. AM 63 fol's Heimskringla is collated against AM 18 fol throughout, and each of its 4,157 variants names that witness. Where a variant has no adopted reading, the other manuscript has text this one lacks.

   An editor's note is a comment: a ligature, a correction the scribe made, a worn passage, a leaf missing from the manuscript. Where the file names the editor responsible, that name appears too.

   Either can be read for the current line or for the whole edition. These are kept out of the text rather than read as part of it - a variant from another manuscript is not a word of this one, and reading them together would silently corrupt word counts, search, and every frequency measure built on them.

Citations
   A manuscript line is cited by leaf and line - "69r.2" is the second line of folio 69 recto. Menota also marks the pages of printed editions in the same files, and those are deliberately not used for citation: "161.11" would mean page 161 of a book published in 1931, which looks exactly like a folio you could find and is not one.

   Verse is cited by line number instead, because that is how verse is cited.

One file, several works
   A manuscript is a physical object that happens to contain whatever was bound into it, not a book with one author. The import shows what it found in each file and lets you merge divisions into a single work, split them, retitle them, or leave them out, before anything is written. Those decisions are saved beside the manuscript and reused next time.
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

        WindowShortcuts.CloseOnEscape(this);
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
