# Classica Codex 3.20.1

Setup no longer offers the medieval French texts.

**[Download for Windows](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
`win-x64` for most PCs, `win-arm64` for Windows on Arm (not yet tried on an Arm
PC). Extract all of it and run `Classica Codex.exe`. Windows will show a blue
"Windows protected your PC" box on first run because the app isn't
code-signed; click More info, then Run anyway.

## Changed

- **"Medieval French Texts (BFM)" is no longer a Setup step.** The Base de
  Français Médiéval publishes its 500 texts one at a time, so the step made 500
  downloads in a row, which took far too long. It is withheld until there is a
  better way to provide the texts. Setup now has one step fewer, and going
  through every step no longer fetches them.
- **If you installed them already, nothing changes.** Every text stays in your
  library and is still listed as Medieval French (BFM) in the library and in
  Search.

## On the Mac and Linux

The Mac preview is still 3.16.3. It shares this app, so this change, and
3.20.0's Go to Citation, reach it with its next release.

1,920 tests, zero warnings.

The downloads are not code-signed, so if you would rather check one than trust it:

```
SHA-256  b385f492f18caa0e3b700d8974dba53a0ef9f2b22abef287acaa1be40b7d38fa  ClassicaCodex-v3.20.1-win-x64.zip
SHA-256  03f5faa0a8368d2b410763201fadf032e67133a174d467ee829f9995ac74bf5c  ClassicaCodex-v3.20.1-win-arm64.zip
```
