> **Historical.** These are the notes for Classica Codex 3.0.1, which is no longer
> available for download. Only the current release is published; get it from
> [the releases page](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest).
> Any download link or SHA-256 below refers to the 3.0.1 ZIP, not to the current
> one, so do not check a current download against a checksum printed here.

3.0.1 is a corpus-accuracy release. Speech attributions were being dropped from
every play in the library — 42,448 of them in the Greek alone, and every Terence
comedy and Shakespeare play besides — along with list entries, colophons and the
Greek Anthology's poet attributions. Plato's attributions had the opposite
problem: they were being counted as vocabulary, so Gorgias read as 4.1% "ΣΩ." by
word count. Text nodes now record what kind of thing they are, which lets the
reader show a play's speakers while the word counts and the stylometry ignore
them.
