#!/bin/sh
# Adds Classica Codex to the desktop's application menu, pointing at this
# folder. It writes one file, in your home folder; run it again if you move
# the folder, and with --remove to take the entry away.
set -eu

here="$(dirname "$(readlink -f "$0")")"
applications="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
entry="$applications/classica-codex.desktop"

if [ "${1:-}" = "--remove" ]; then
  rm -f "$entry"
  echo "Removed $entry. The app itself is still in $here."
  exit 0
fi

mkdir -p "$applications"
sed "s|__HERE__|$here|g" "$here/classica-codex.desktop" > "$entry"
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$applications" || true
fi
echo "Classica Codex is in your application menu."
