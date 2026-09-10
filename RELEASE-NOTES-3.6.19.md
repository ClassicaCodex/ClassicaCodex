# Classica Codex 3.6.19

You can now choose where the downloads go, not just where the library lives.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run. An existing install
keeps using the folder it already has, and is not asked anything.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## Where nine gigabytes goes was not your decision

Setting this program up downloads about **9 GB**: the texts, the dictionaries,
and — much the largest part, 5.5 GB of it — the Greek and Latin word-form data.
The database has been movable for a long time. The downloads were not. They went
to `Documents\ClassicaCodexData` and nowhere else, which is on the system drive,
which is exactly the drive somebody with a small SSD does not have nine spare
gigabytes on.

Guided Setup now has a **Download Folder** step, straight after the database step
and before anything is fetched — because asking later would be asking you to move
gigabytes you had already downloaded. Advanced Setup has a **Download Folder**
button beside Database Location.

The step tells you how much room is free where you have pointed it, and says so
plainly when that is less than a full set needs. If your temporary folder is on a
different drive, it mentions that drive's free space too: the biggest collections
are unpacked through `%TEMP%` on the way in, so the download needs room in two
places, and being told "500 GB free" about the destination while the system drive
quietly fills is exactly the reassurance that precedes a failure an hour later.

The folder is chosen, not merely typed at: it is created if it does not exist and
written to once to prove it can be, because a folder that turns out to be
read-only should say so while you are still looking at the screen rather than at
the start of an hour-long fetch.

## What happens to what you have already downloaded

**Nothing.** Choosing a different folder decides where future downloads go. It
moves no files, deletes nothing, and does not affect your library at all — the
texts you have ingested live in the database, not in those folders. No step will
ask you to download anything again.

Three things do read from that folder rather than only downloading into it, and
the program now says so when you change it:

- **the map**, which is read afresh every time you open it rather than being
  ingested — under a megabyte to fetch again;
- **adding Stephanus and Bekker numbers**, which reads the texts back out of the
  folder rather than fetching anything, so it finds nothing in an empty one;
- **Medieval Nordic manuscripts**, which is the one that matters: those files and
  the work divisions you confirmed for them are your own work, not a download.
  They are worth copying across by hand rather than redoing.

That last point corrects something the help text has always said. It described
everything in that folder as disposable working copies of public data. That is
true of all of it except the Menota material, and the help now says which.

## Checks

**1,222 tests, zero warnings on a clean build**, nineteen new. They cover the
part that can be tested without a window: whether a folder will actually take a
download — that it is created when missing, that the write test leaves nothing
behind, that nonsense is refused with a reason rather than an exception, and
that a folder which does not exist *yet* still counts as usable, since on a fresh
install the default never exists and greeting a newcomer with a red cross beside
a perfectly good default would be the wrong first impression.

The stored preference itself is deliberately not exercised by the tests: writing
it would mean creating a real file in your own settings folder, which tests have
no business doing.

Three things were checked and found not to need changing, which is worth as much
as a fix: nothing else in the program reads that folder at runtime — every ingest
service is handed the destination and stores its results in the database, so a
path change cannot orphan a library; fourteen of the fifteen setup steps decide
"already done?" by asking the database rather than the disk, so they stay ticked
when the folder moves; and no test or tool hardcodes the old location.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  86703BACA49C12A8D338FE056828470C029C31FFC6892164DB59642ACD8FF965
```
