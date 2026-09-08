# Classica Codex 2.0

---

## Translate it yourself

The headline of this release. Right-click any work in the library and choose
**Translate This Myself**.

You get the passage, a list of its words, and your own translation box. Click
any word for its dictionary headword, its grammatical parse — case, tense, mood,
voice — and its LSJ or Lewis & Short entry. All of it is looked up from the data
you already have, not generated, so it's the same information a printed
commentary would give.

Some deliberate choices, because this is meant to be a way to learn rather than
a way to produce output quickly:

- **The passage before and after are shown**, dimmed, around the one you're
  working on. Citation references cut across sentences constantly in verse — a
  relative pronoun at the end of one line resolves in the next — and translating
  a clause with half of it off screen is how you end up confidently wrong.
- **AI help never types into your box.** *AI Translate Passage* renders the whole
  passage; *AI Translate Word* explains which sense of a word is in play in this
  particular line. Both appear in the reference panel beside your work. An answer
  that arrives already typed stops being something to weigh and becomes the
  answer.
- **Comparing against a published translation is locked until you've written
  something.** Reading someone else's rendering first is the one action that
  quietly removes the point of the exercise.
- **An alphabet reference** for the script you're working in — the letters, and
  the breathings, accents and iota subscript that sit on them. It doesn't help
  with meaning. It helps with the step before meaning, when you can't yet read
  the script well enough to look a word up at all.

Each passage saves as you move on, so a work can be picked at over months.
Reopening starts at the first passage you haven't done. **Go to** jumps anywhere
in the work and doubles as the progress view. Your translation becomes an edition
like any other — it appears in the reader, in Compare Translations, in search,
and in export.

If a work has more than one original-language edition, you're asked once which
you're translating from; that decides the citation references your work is filed
under. Which translation to check against can be changed freely, and the app
suggests the one whose references line up with your text.

## Elsewhere

- **Recent searches.** The last ten searches you ran, with every filter, recorded
  automatically. Nothing to save, nothing to tidy up. Running a search again
  moves it back to the top rather than listing it twice.
- **Picks up where you left off.** Reopens the passage you were last reading.
  Can be switched off under *Reading* in the Setup Wizard if you'd rather it
  didn't — your place is still remembered either way.
- **Filter the library by author.** A box beside the Library button; with a few
  thousand authors loaded, typing three letters beats scrolling.
- **A new icon set**, with separate artwork for light and dark themes, and a
  parchment light theme to go with it. The toolbar is icons only now, with the
  labels on hover.
- **The word index moved to its own window.** Checking whether it's up to date
  counts across the whole library, and that check was running every time the
  Setup Wizard opened. Now it only runs when you ask for it.
- **AI translation settings** are reachable from the Setup Wizard directly.

## Upgrading

The database schema has moved through six migrations. Upgrading is automatic on
first launch and carries everything forward — tags, bookmarks, notes, and any
translations already generated.

If you keep more than one library file, each upgrades independently the first
time you open it.

## Notes

Everything network-related remains opt-in. The app works completely offline; AI
translation does nothing unless you've entered a key and asked for it, and
Read Aloud uses voices already installed on Windows.

The application code is MIT licensed. The texts, lexica, maps and artefact
metadata it downloads at setup are not — those come from the Perseus Digital
Library, Open Greek and Latin, Princeton WordNet and Natural Earth, each under
its own terms. Some of it is non-commercial, which is one reason this
application is free and stays that way.

