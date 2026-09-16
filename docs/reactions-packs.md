# Writing your own Fictional Ancient Reactions

A debate is one JSON file. Put it in a folder called `Reactions` next to
`ClassicaCodex.UI.exe` and it is loaded at startup along with the ones that
ship in the program.

A file that does not validate is **skipped, not repaired** — a half-loaded
debate would read as the whole argument, and the turn that got dropped could
be the one carrying a citation. Anything rejected is listed under **What am I
reading?** in the window, with the reason.

---

## The shape of a file

```json
{
  "critics": [
    {
      "id": "demeas-acharnae",
      "name": "Demeas",
      "kind": "Composite",
      "era": "Classical Athens",
      "floruitStart": -458,
      "floruitEnd": -412,
      "place": "Acharnae",
      "role": "charcoal-burner and smallholder",
      "sketch": "Two or three sentences. Shown on the persona card.",
      "tastes": "What they value, and what they cannot stand.",
      "avatar": "skin:tan;hair:grey;beard:full;hat:none;cloth:#7b5230;accent:#3f2a16"
    }
  ],

  "debate": {
    "id": "clouds-423",
    "work": { "authorKey": "aristophanes", "titleKeys": ["clouds", "nubes"] },
    "title": "Third place, and what it was for",
    "kind": "Scene",
    "compositionYear": -423,
    "settingYear": -423,
    "settingPlace": "Athens",
    "settingNote": "A paragraph setting the scene. Worth using to say what is known and what is guessed."
  },

  "turns": [
    {
      "seq": 1,
      "criticId": "demeas-acharnae",
      "text": "What they say.",
      "passageRef": "98",
      "sourceCitation": "Only where a view is attested.",
      "sourceWork": { "authorKey": "plato", "titleKeys": ["apology"], "citationRef": "19" }
    }
  ]
}
```

Years are negative for BCE and positive for CE. There is no year zero, and
none is needed: the arithmetic is done in plain integers and only the display
converts.

## Naming the work

`work` is matched against the library by author name and title text, **not by
CTS URN**. The same work carries different URNs across Perseus, Open Greek and
Latin, and First1KGreek, and which of those a reader installed varies — a pack
keyed on one corpus's URN would silently have no debates for somebody who
installed another.

- `authorKey` matches a whole word in the author's name first, and falls back
  to a substring, which is what `vergil` inside *P. Vergilius Maro (Virgil)*
  needs. An author whose name contains "pseudo" never matches.
- `titleKeys` — any one of them matching is enough. Give the alternatives:
  `["metamorphos"]` catches both *Metamorphoses* and *Metamorphoseon*.

## Citing a passage

`passageRef` is a citation in the work under discussion, written the way a
classicist writes it: `1.1` for the *Iliad*, `225` for a play, `1.22.1` for
Thucydides. It is resolved against whatever edition the reader actually has.

Three things are handled for you, and you do not need to write around any of
them:

- Perseus stores the whole edition URN in most citation references, so
  `1.1` is matched as a suffix of
  `urn:cts:greekLit:tlg0012.tlg001.perseus-grc2.1.1`.
- Prose is often stored a line at a time *inside* the section you are citing —
  Thucydides 1.22.1 exists in the file only as `1.22.1.1` — so a reference is
  also matched as a dot-boundary prefix and lands on the first line under it.
- The original-language edition always wins over a translation.

A reference that resolves to nothing is shown as plain text saying so, rather
than as a link that does nothing. That is the normal case for a reader who has
not installed that corpus, and it is not an error.

## The two kinds of debate

**`Scene`** — one room, one evening. Needs `settingYear`. Every speaker must
have been alive in that year and the year must not be before
`compositionYear`. Turns do not carry years of their own.

**`AcrossTheCenturies`** — voices from different centuries, in the order they
spoke. No `settingYear`; every turn carries its own `year`, the years may not
go backwards, and each speaker must have been alive in the year of their own
turn. The window labels it as centuries of reaction rather than a
conversation, and puts a date between turns when the date changes.

The second kind exists because the first cannot express what it is often most
worth showing. Nobody was alive for both Xenophanes and Longinus, and the
argument about Homer runs through both.

## Composite and Historical

This is the part that matters.

**`Composite`** is an invented person. Invent freely: the invented speakers are
there to voice the concerns of the audiences and readers who really did argue
about these works and left nothing in writing.

**`Historical`** is somebody who existed. A historical critic may only be given
a view that is **attested**, and every one of their turns needs a
`sourceCitation`. The validator enforces the citation; it cannot check that
the stance is real, and that part is on you.

The reason for the rule: this program is otherwise full of real ancient text,
and a reader has no way to tell a plausible invention in Plato's mouth from
something Plato wrote. The citation is what makes it checkable.

Keep the *stance* attested and the *wording* your own. The wording is
paraphrase and the window says so.

`sourceWork` is optional and turns the citation into a link where the reader
has that text. It takes the same author/title matching as `work`, plus a
`citationRef` in the shape that work is *stored* in — which is not always the
shape it is *cited* in. Augustine's *Confessions* 1.13.21 is stored as `1.21`
in the Open Patristics text, so the pack prints `Confessions 1.13.20-21` and
links `1.21`.

## Portraits

`avatar` is a recipe, not a file. Unknown values fall back rather than fail.

| trait | values |
|---|---|
| `skin` | `light` `olive` `tan` `brown` `dark` |
| `hair` | `dark` `brown` `red` `grey` `white` `bald` |
| `beard` | `none` `short` `full` |
| `hat` | `none` `laurel` `fillet` `veil` `cap` |
| `cloth` | any `#rrggbb` — also tints the speaker's bubble |
| `accent` | any `#rrggbb` — trim, wreath, headband |

`cloth` does double duty: it is the garment and it is the colour the speaker's
messages are tinted with, which is how a reader follows a six-way conversation
without reading the name each time. Give everybody in one debate a different
one.

## The rules, in full

Checked on load, and by a test per rule:

1. Sequence numbers start at 1 and have no gaps.
2. Between 6 and 14 turns.
3. At least two distinct speakers.
4. Every `criticId` is declared, in this pack or another.
5. Every turn says something.
6. A floruit does not end before it begins.
7. A `Scene` has a `settingYear`, at or after `compositionYear`, inside every
   speaker's floruit — and its turns carry no years of their own.
8. An `AcrossTheCenturies` debate dates every turn, never goes backwards, and
   keeps each turn inside its own speaker's floruit.
9. A `Historical` critic never speaks without a `sourceCitation`.
10. A citation reference looks like one.
11. A linked `sourceWork` has a `sourceCitation` to print as its label.

Critics are shared between packs by `id`, so the same chorus-trainer can turn
up in two debates — but the two declarations have to agree.
