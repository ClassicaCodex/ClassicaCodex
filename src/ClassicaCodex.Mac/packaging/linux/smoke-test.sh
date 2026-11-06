#!/bin/bash
# Starts a built tarball on a virtual display, the way a reader would, in an
# empty home folder, with a library already chosen. Then it presses the keys a
# reader would press and photographs what happens. The Linux workflow runs it;
# on a Linux machine with Xvfb, openbox, xdotool and ImageMagick it runs as it
# stands:
#
#   src/ClassicaCodex.Mac/packaging/linux/smoke-test.sh <tarball> <library.db> <photo folder>
#
# Fails if the app does not stay up, or writes anything to its error log.
set -euo pipefail

tarball="$(readlink -f "$1")"
library="$(readlink -f "$2")"
photos="$3"
mkdir -p "$photos"
photos="$(readlink -f "$photos")"

home="$(mktemp -d)"
export HOME="$home"
export XDG_DATA_HOME="$home/.local/share"
preferences="$XDG_DATA_HOME/ClassicaCodex"
mkdir -p "$preferences"
printf '%s' "$library" > "$preferences/database-path.txt"

# Opens on a Greek passage, by way of the setting that reopens the last one
# read, so that the first photograph shows polytonic Greek in the reader.
python3 - "$library" > "$preferences/reading-position.txt" <<'PYTHON'
import sqlite3, sys
work, line = sqlite3.connect(sys.argv[1]).execute("""
    SELECT w.CtsUrn, t.CitationRef
    FROM TextNodes t
    JOIN Editions e ON e.EditionId = t.EditionId
    JOIN Works w ON w.WorkId = e.WorkId
    WHERE e.Language = 'grc' AND length(t.Text) > 30
    ORDER BY e.EditionId, t.SortOrder
    LIMIT 1""").fetchone()
print(work)
print(line)
PYTHON

tar -xzf "$tarball" -C "$home"
app="$(echo "$home"/classica-codex-*/classica-codex)"

export DISPLAY=:99
Xvfb "$DISPLAY" -screen 0 1440x900x24 &
sleep 2
openbox &
sleep 1

"$app" > "$photos/app.log" 2>&1 &
pid=$!

photo() { import -window root "$photos/$1.png"; }
alive() {
  if ! kill -0 "$pid" 2>/dev/null; then
    echo "The app stopped: $1" >&2
    cat "$photos/app.log" >&2
    exit 1
  fi
}
press() { # keys, then a name for the photograph
  xdotool key --clearmodifiers "$1"
  sleep 4
  alive "after $1"
  photo "$2"
}

sleep 20
alive "starting up"
photo 01-opened

# Whether the taskbar can match the window to its desktop entry.
xdotool search --class classica-codex > "$photos/windows-by-class.txt"
test -s "$photos/windows-by-class.txt"
xdotool search --class classica-codex windowfocus --sync || true
sleep 1

press ctrl+comma 02-settings-from-its-key
press Escape 03-settings-closed
press ctrl+f 04-search-from-its-key
press Escape 05-search-closed
press ctrl+shift+t 06-all-tools-from-its-key

if [ -e "$preferences/errors.log" ]; then
  cp "$preferences/errors.log" "$photos/"
  echo "The app logged errors:" >&2
  cat "$preferences/errors.log" >&2
  exit 1
fi

kill "$pid"
echo "It ran, and logged nothing."
