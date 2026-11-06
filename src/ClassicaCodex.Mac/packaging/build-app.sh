#!/bin/bash
# Builds "Classica Codex.app" and a .dmg to install it from. Run on a Mac,
# from anywhere:
#
#   src/ClassicaCodex.Mac/packaging/build-app.sh            # this Mac's architecture
#   src/ClassicaCodex.Mac/packaging/build-app.sh x64        # Intel
#   src/ClassicaCodex.Mac/packaging/build-app.sh arm64      # Apple silicon
#
# Needs the .NET 10 SDK (or newer) and the tools every Mac has: sips,
# iconutil, codesign, hdiutil. Writes to src/ClassicaCodex.Mac/bin/mac-app/, which git ignores.
#
# The app is self-contained - the .NET runtime travels inside it - and how it
# is signed depends on what this Mac has:
#
# - With a Developer ID Application certificate in the keychain, it is signed
#   with that, under the hardened runtime, which is what notarisation needs.
#   SIGN_IDENTITY picks one certificate where there are several.
# - With NOTARY_PROFILE as well, naming a profile made once with
#     xcrun notarytool store-credentials <profile>
#   the app and the disk image are sent to Apple, notarised and stapled. A
#   download then opens on any Mac without Gatekeeper stopping it:
#     NOTARY_PROFILE=<profile> src/ClassicaCodex.Mac/packaging/build-app.sh arm64
# - Without a certificate, or with SIGN_IDENTITY=-, it is signed ad hoc, which
#   is what Apple silicon requires of anything it runs. A copy built on this
#   Mac runs straight away; a downloaded copy is held by Gatekeeper the first
#   time, and System Settings → Privacy & Security has an Open Anyway button
#   for it, the Mac's version of the blue SmartScreen box.
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
entitlements="$here/ClassicaCodex.entitlements"

identity="${SIGN_IDENTITY:-$(security find-identity -v -p codesigning | awk -F'"' '/Developer ID Application/ { print $2; exit }')}"
identity="${identity:--}"
notary="${NOTARY_PROFILE:-}"
if [ "$identity" = "-" ] && [ -n "$notary" ]; then
  echo "NOTARY_PROFILE is set, but there is no Developer ID Application certificate to sign with." >&2
  echo "Create one in Xcode → Settings → Accounts → Manage Certificates → + → Developer ID Application." >&2
  exit 1
fi

# Sends a file to Apple's notary service and waits for the verdict. notarytool
# exits 0 for a rejection as well as an acceptance, so the status is read from
# its answer, and a rejection prints Apple's log of what it objected to.
notarise() {
  local result id status
  result="$(xcrun notarytool submit "$1" --keychain-profile "$notary" --wait --output-format json)"
  id="$(/usr/bin/plutil -extract id raw -o - - <<<"$result")"
  status="$(/usr/bin/plutil -extract status raw -o - - <<<"$result")"
  echo "Notarisation of $(basename "$1"): $status ($id)"
  if [ "$status" != "Accepted" ]; then
    xcrun notarytool log "$id" --keychain-profile "$notary" >&2 || true
    exit 1
  fi
}

echo "Classica Codex $version for $rid"
if [ "$identity" = "-" ]; then
  echo "Signing: ad hoc (no Developer ID Application certificate)"
else
  echo "Signing: $identity${notary:+, notarising with profile $notary}"
fi
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

if [ "$identity" = "-" ]; then
  codesign --force --deep --sign - "$app"
else
  # Inside out, as Apple asks: every file .NET puts beside the executable,
  # then the bundle, which signs the app's own executable with the
  # entitlements. macOS counts everything in Contents/MacOS as code, so the
  # .dll and .json files need a signature of their own as well as the native
  # binaries - the runtime, SkiaSharp, libgit2, SQLite and the rest. A file that
  # is not native code keeps its signature in extended attributes, which the
  # disk image and a drag into Applications both carry.
  while IFS= read -r -d '' file; do
    if file -b "$file" | grep -q 'Mach-O'; then
      codesign --force --timestamp --options runtime --sign "$identity" "$file"
    else
      codesign --force --timestamp --sign "$identity" "$file"
    fi
  done < <(find "$app/Contents/MacOS" -type f ! -name ClassicaCodex.Mac -print0)
  codesign --force --timestamp --options runtime --entitlements "$entitlements" \
    --sign "$identity" "$app"
fi
codesign --verify --deep --strict "$app"

# Notarised and stapled before it goes into the disk image, so that a copy
# dragged into Applications carries its ticket with it and opens even on a
# Mac that is offline the first time.
if [ -n "$notary" ]; then
  zip="$out/ClassicaCodex-$version-mac-$arch-notarise.zip"
  rm -f "$zip"
  ditto -c -k --keepParent "$app" "$zip"
  notarise "$zip"
  rm -f "$zip"
  xcrun stapler staple "$app"
fi

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

if [ "$identity" != "-" ]; then
  codesign --force --timestamp --sign "$identity" "$dmg"
fi
if [ -n "$notary" ]; then
  notarise "$dmg"
  xcrun stapler staple "$dmg"
  spctl --assess --type open --context context:primary-signature --verbose "$dmg"
fi

echo
echo "App:  $app"
echo "DMG:  $dmg"
shasum -a 256 "$dmg"
