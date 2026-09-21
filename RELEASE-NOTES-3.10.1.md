# Classica Codex 3.10.1

A setup step could not be stopped once it had started, a batch stylometry run
deleted other authors' saved runs, and the default download folder was probably
inside OneDrive.

Nothing here changes your data. **No schema change, no re-ingest, no re-index**,
and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

> If you set up a **Latin** library before 3.7.2 and have not re-run the Latin
> Lemma Data step yet, it is still worth six minutes — see the note in the
> README.

## Setup steps can be stopped

Start a download in either setup window and there was no way out of it. Back,
Next and the step's own button were all switched off for the duration, and the
longest step says on its own face that it takes about an hour. Closing the
window did not help: it went away and the download carried on, still filling the
folder, still writing to the database the reader was about to open, with nothing
on screen to say so. Task Manager was the only remaining answer.

All the machinery for stopping was already in place — the cancellation was
threaded through every download and every ingest, and the code that reports
"Stopped." was sitting there waiting. Nothing ever called it.

Now the step's own button becomes **Cancel** while it runs. Closing the window
asks first, and then stays up until the step has actually stopped before closing
itself, so the window going away is your confirmation rather than a hope.

## What that turned up, which is the point of shipping it

The first person to press Cancel watched the machine carry on downloading.

The large collections arrive as a git download, and there are two ways to fetch
one: a shallow copy of just the current files, or a full copy including every
past revision of every file. Shallow is far smaller and is tried first, with the
full copy as a fallback for servers that cannot do shallow.

Cancelling aborts a transfer, and an aborted transfer and an unsupported one
arrive as the same kind of error. So pressing Cancel was read as "this server
cannot do shallow", and was answered by throwing away the partial download and
starting a **full** copy of the same repository — several times larger than the
download that had just been cancelled. The status line said "fetching in full
instead", which was an accurate report of a decision made for entirely the wrong
reason.

Neither half of that was wrong on its own, and no amount of reading either would
have found it. It needed somebody to press the button.

Two smaller ones came out of the same testing. The button went on saying
"Cancel" for up to half a minute after the step stopped — and in that window
pressing it started the download again, because the label was the only thing
still claiming a step was running. And the word-index step overwrote its own
"Stopped." message a moment after showing it.

## Batch stylometry no longer deletes other authors' runs

A batch run clears the previous runs for the author it is about to re-run, so
that running it twice does not put each work into the reference distribution
twice. It cleared by language and settings and not by author.

The settings form resets to the same defaults every time it opens, so two
authors batched one after another always share a settings profile. Batch
Sophocles, then batch Euripides, and the seven Sophocles runs were deleted —
silently, with no count reported, no undo, and no delete-run command anywhere in
the program to have made the loss expected. Each run costs a full pass over the
corpus to rebuild, and comparing authors across saved runs is the entire reason
the Compare Saved Runs window exists, so this fired the first time anyone used
the feature the way it was meant to be used.

## The download folder and OneDrive

The default download folder was inside Documents. On a Windows 11 machine signed
into a Microsoft account, Documents is usually inside OneDrive — so the default
put about nine gigabytes of small files somewhere they would be uploaded to an
account whose free tier is five. Quota warnings, your other backed-up files
quietly ceasing to sync, and on a metered connection an actual bill, ten minutes
after installing a reading application. The free-space check could not warn you:
it measures the local disk and would have said "250 GB free" quite correctly.

None of it is worth syncing. These folders hold public source files that can be
downloaded again; your library, tags and bookmarks are in the database, which
lives somewhere OneDrive never touches.

If your Documents is synced and you have not downloaded anything yet, the wizard
now suggests a folder outside it and says why. **If you already have downloads
where they are, nothing moves** — the folder you are using stays the folder you
are using.

## Seneca's Octavia

The library's table of disputed and spurious works gave the reason for rejecting
the *Octavia* as "Dramatises Seneca's own death, which settles it." It does not.
Seneca appears in the play alive, arguing with Nero about clemency, and the
action ends with Octavia being taken to Pandateria.

The real argument is the prophecy: Agrippina's ghost foretells Nero's death, and
the manner of it, in detail — an event of 68, three years after Seneca was made
to open his veins. That is what the entry says now.

Twenty-eight other entries in that table were checked and are sound. This one
was wrong for six versions, and it is the kind of wrong that a reader who knows
the text spots immediately and then reasonably distrusts the rest for.

## Also

The notes for 3.10.0 claimed the Almagest engine agreed with Ptolemy's own
worked example "to within a second of arc". The two figures printed in that same
sentence are half an arcminute apart, which is thirty times the claim. His tables
are printed to the arcminute, so what is true — and still worth saying — is that
the engine agrees with them to the precision he wrote down. Corrected here and on
the 3.10.0 page.

1,452 tests, zero warnings.
