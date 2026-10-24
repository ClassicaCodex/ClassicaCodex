#!/bin/bash
# Builds "Classica Codex.app" and a .dmg to install it from. Run on a Mac,
# from anywhere:
#
#   src/ClassicaCodex.Mac/packaging/build-app.sh            # this Mac's architecture
#   src/ClassicaCodex.Mac/packaging/build-app.sh x64        # Intel
#   src/ClassicaCodex.Mac/packaging/build-app.sh arm64      # Apple silicon
#
# Needs the .NET 8 SDK (or newer) and the tools every Mac has: sips,
# iconutil, codesign, hdiutil. Writes to src/ClassicaCodex.Mac/bin/mac-app/, which git ignores.
#
# The app is self-contained - the .NET runtime travels inside it - and signed
# ad hoc, which is what Apple silicon requires of anything it runs. It is not
# notarised: that needs a paid Apple Developer account. A copy built on this
# Mac runs straight away; a copy downloaded from elsewhere is held by
# Gatekeeper the first time, and System Settings → Privacy & Security has an
# Open Anyway button for it, the Mac's version of the blue SmartScreen box.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../../.." && pwd)"
project="$repo/src/ClassicaCodex.Mac/ClassicaCodex.Mac.csproj"

arch="${1:-$(uname -m)}"
case "$arch" in
  arm64|aarch64) rid="osx-arm64"; arch="arm64" ;;
  x86_64|x64)    rid="osx-x64";   arch="x64" ;;
  *) echo "Unknown architecture: $arch (use arm64 or x64)" >&2; exit 1 ;;
esac

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project" | head -1)"
out="$repo/src/ClassicaCodex.Mac/bin/mac-app"
publish="$out/publish-$arch"
app="$out/Classica Codex.app"

echo "Classica Codex $version for $rid"
rm -rf "$publish" "$app"
mkdir -p "$out"

dotnet publish "$project" -c Release -r "$rid" --self-contained true \
  -p:UseAppHost=true -p:DebugType=none -o "$publish"

mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish/." "$app/Contents/MacOS/"
sed "s/__VERSION__/$version/g" "$here/Info.plist" > "$app/Contents/Info.plist"

# The icon, from the 1024-pixel master in the artwork folder.
art="$repo/src/ClassicaCodex.Mac/Assets/Art/app-icon-1024.png"
iconset="$out/ClassicaCodex.iconset"
rm -rf "$iconset" && mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z $size $size "$art" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z $double $double "$art" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/ClassicaCodex.icns"
rm -rf "$iconset"

codesign --force --deep --sign - "$app"

# The disk image: the app beside a link to Applications, to drag one onto
# the other.
staging="$out/dmg-staging"
rm -rf "$staging" && mkdir -p "$staging"
cp -R "$app" "$staging/"
ln -s /Applications "$staging/Applications"
dmg="$out/ClassicaCodex-$version-mac-$arch.dmg"
rm -f "$dmg"
hdiutil create -volname "Classica Codex" -srcfolder "$staging" -ov -format UDZO "$dmg" >/dev/null
rm -rf "$staging"

echo
echo "App:  $app"
echo "DMG:  $dmg"
shasum -a 256 "$dmg"
