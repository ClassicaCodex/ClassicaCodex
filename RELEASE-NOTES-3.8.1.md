# Classica Codex 3.8.1

The setup wizard was badly laid out on any display above 100% scaling, and
nobody had ever run it on one.

Nothing here changes your data. **No schema change, no re-ingest, no re-index**,
and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

> If you set up a **Latin** library before 3.7.2 and have not re-run the Latin
> Lemma Data step yet, it is still worth six minutes — see the note in the
> README.

## The setup wizard at 125%, 150% and 200%

Everything the wizard shows sits in one panel, and the method that lays that
panel out assigns eleven coordinates every time a step changes. All eleven were
written as measurements taken on a 100% display.

That method runs *after* Windows Forms has already scaled the window, so above
100% it put the 100% layout back over the scaled one. What you saw depended on
the step:

- On **Database Location** and **Download Folder**, the editable path was moved
  up into the description paragraph above it, while the **Browse** button beside
  it — which is positioned once when the window is built and was never moved
  again — stayed where the scaling had left it. The pair came apart, and the box
  ended up inside the text. Measured: the path overlapped the description by 15
  pixels at 150% and 40 at 200%.

- On the same two steps the action button, the progress bar and both status
  lines were drawn on top of one another.

- On the **Menota** step, whose entire instruction is to download a file into
  the folder shown below, the folder box was positioned inside the description
  panel — and since that panel is drawn in front, the folder was painted over
  completely. The step told you to put a file somewhere and showed you nowhere.

All eleven coordinates are now scaled. Two of them are additionally placed from
the bottom of the thing above them rather than from a number, so a control
cannot land inside a paragraph again whatever the arithmetic says, and the
Browse button is positioned from the box it belongs to.

The reactions window added in 3.8.0 had the same mistake in its own header —
cosmetic there, the same cause — and is fixed too.

## Why this had never been seen

At 100% the scaled layout and the design layout are the same layout, so there
is nothing to see. Every machine this had been written, reviewed, screenshotted
and tested on was at 100%. It took installing it on a laptop.

The tests now simulate a high-DPI display rather than assuming one: they run
the same scaling pass Windows performs, and check relationships instead of
numbers — the path row sits below the description, the button stays level with
its box, nothing is drawn on top of anything else, and the step that names a
folder actually shows one. Every step, at 100%, 125%, 150% and 200%.

Each of those was confirmed by putting the bug back and watching the test fail
at every scaling above 100% and pass at 100%, which is the only way to know a
test of this kind is worth having.

A sweep of every other window found no other coordinate assigned outside a
constructor, so this was the one place it could happen.

1,385 tests, zero warnings.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  608D8634EAA2F32489AE9B32F435559B4334C89AA780E2A8C6CD91BF94DE9260
```
