# Classica Codex 3.6.1

The reference is in the margin now, where an edition puts it.

3.6.0 made Plato citable and then showed the citation only if you hovered over
the line — which is the wrong place for the one thing a reader needs in order
to cite what they are reading, or to find their way back to it a week later.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Upgrading is extract-and-run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## A line number, at last

This reader has never shown one. Reading the *Iliad* in it, there was no way to
tell which line you were on without stopping to hover over it — which is a
strange thing to have to say about a tool for reading classical texts, and it
is fixed.

Verse is numbered every fifth line, the way an Oxford text sets it:

```
  1  Ἥκω νεκρῶν κευθμῶνα καὶ σκότου πύλας
     λιπών, ἵν’ Ἅιδης χωρὶς ᾤκισται θεῶν,
     Πολύδωρος, Ἑκάβης παῖς γεγὼς τῆς Κισσέως
     Πριάμου τε πατρός, ὅς μ’, ἐπεὶ Φρυγῶν πόλιν
  5  κίνδυνος ἔσχε δορὶ πεσεῖν Ἑλληνικῷ,
```

A new book prints in full — `2.1` — because the count starts again there and a
bare `5` would otherwise mean something different from the `5` above it.

## Plato and Aristotle get their letters

Where a text carries Stephanus or Bekker pagination, the mark goes where the
section changes and nowhere else:

```
 2a  τί νεώτερον, ὦ Σώκρατες, γέγονεν, ὅτι σὺ τὰς ἐν Λυκείῳ…
     ΣΩ.
     οὔτοι δὴ Ἀθηναῖοί γε, ὦ Εὐθύφρων, δίκην αὐτὴν καλοῦσιν…
     ΕΥΘ.
 2b  τί φῄς; γραφὴν σέ τις, ὡς ἔοικε, γέγραπται…
```

The *Republic* reads 327a, 328a, 329a down its margin, which is the sequence on
a printed page.

**Not printing it beside every line is the point**, and it is also the hard
part. A Platonic dialogue puts a speech attribution between every pair of
lines. Comparing each line with the one directly above it would find a speaker
carrying no reference, conclude the section had changed, and stamp 2a beside
every line of the *Euthyphro* — a column of noise a reader would stop seeing
within a page. Each line is compared with the nearest **line** above it
instead.

Nothing is marked beside a speaker or a stage direction. Those are not lines an
editor numbers. And a reference too long for a margin — Menota cites a
manuscript as `text=F:book=1:letter=9.1` — falls back to the line number rather
than becoming a second column.

## If you would rather not have it

Right-click in the reader and untick **Citations in the margin**. It sits
beside the *Show* submenu rather than inside it, because that submenu hides
itself for an edition with only one kind of node — which is most verse, and
verse is exactly where a line number earns its place.

Turning it off re-measures the page rather than reloading it, so you keep your
place in the work. Turning it back on does the same.

## Checks

The margin is measured from the reading font, so it follows both the reading
size and the system text size. Checked at the 150% equivalent, where `1094a15`
— the widest Bekker mark in this corpus — still fits without clipping.

Rendered against a full 2.3-million-line library in light and dark, with a
marked line selected and unselected, at the reading size and at 150%, and with
the margin turned off to confirm the page returns to exactly what it was.

**1,026 tests, zero warnings on a clean build** — twenty of them on when a mark
appears at all, which is the whole of the design.
