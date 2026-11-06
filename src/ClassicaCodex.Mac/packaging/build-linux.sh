#!/bin/bash
# Builds a tarball of Classica Codex for Linux. Run on Linux or a Mac, from
# anywhere:
#
#   src/ClassicaCodex.Mac/packaging/build-linux.sh            # x64
#   src/ClassicaCodex.Mac/packaging/build-linux.sh arm64
#
# Needs the .NET 8 SDK (or newer). Writes to src/ClassicaCodex.Mac/bin/linux-app/,
# which git ignores.
#
# The app is self-contained - the .NET runtime travels inside it - so unpacking
# it is all the installing there is. packaging/linux/README.txt, which goes
# into the tarball, lists what it needs from the system and what is optional.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../../.." && pwd)"
project="$repo/src/ClassicaCodex.Mac/ClassicaCodex.Mac.csproj"

arch="${1:-x64}"
case "$arch" in
  x64|x86_64)    arch="x64" ;;
  arm64|aarch64) arch="arm64" ;;
  *) echo "Unknown architecture: $arch (use x64 or arm64)" >&2; exit 1 ;;
esac
rid="linux-$arch"

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project" | head -1)"
out="$repo/src/ClassicaCodex.Mac/bin/linux-app"
name="classica-codex-$version-linux-$arch"
stage="$out/$name"

echo "Classica Codex $version for $rid"
rm -rf "$stage" "$stage.tar.gz"
mkdir -p "$stage/lib"

dotnet publish "$project" -c Release -r "$rid" --self-contained true \
  -p:UseAppHost=true -p:DebugType=none -o "$stage/lib"

cp "$here/linux/classica-codex" "$here/linux/install.sh" "$here/linux/classica-codex.desktop" "$here/linux/README.txt" "$stage/"
cp "$repo/src/ClassicaCodex.Mac/Assets/Art/app-icon-1024.png" "$stage/classica-codex.png"
cp "$repo/LICENSE" "$repo/THIRD-PARTY-NOTICES.md" "$repo/THIRD-PARTY-NOTICES-DOTNET.txt" \
  "$repo/THIRD-PARTY-NOTICES-LIBGIT2.txt" "$repo/src/ClassicaCodex.Mac/THIRD-PARTY-NOTICES-MAC.md" "$stage/"
chmod +x "$stage/classica-codex" "$stage/install.sh" "$stage/lib/ClassicaCodex.Mac"

# COPYFILE_DISABLE keeps a Mac's tar from adding its ._ files.
COPYFILE_DISABLE=1 tar -C "$out" -czf "$stage.tar.gz" "$name"

echo
echo "Tarball: $stage.tar.gz"
if command -v sha256sum >/dev/null 2>&1; then sha256sum "$stage.tar.gz"; else shasum -a 256 "$stage.tar.gz"; fi
