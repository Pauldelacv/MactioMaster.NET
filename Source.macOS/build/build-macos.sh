#!/usr/bin/env bash
#
# Builds RatioMaster.app for macOS.
#
#   ./build-macos.sh                 universal binary (arm64 + x86_64)
#   ./build-macos.sh arm64           Apple Silicon only
#   ./build-macos.sh x86_64          Intel only
#   ./build-macos.sh universal --dmg also produce a disk image
#
# Requires the .NET 8 SDK. Everything else (sips, iconutil, lipo, codesign,
# hdiutil) ships with Xcode command line tools.

set -euo pipefail

ARCH="${1:-universal}"
MAKE_DMG=false
for arg in "$@"; do
  [[ "$arg" == "--dmg" ]] && MAKE_DMG=true
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_DIR="$(cd "$ROOT_DIR/.." && pwd)"
PROJECT="$ROOT_DIR/RatioMaster.App/RatioMaster.App.csproj"
OUT_DIR="$ROOT_DIR/artifacts"
APP_DIR="$OUT_DIR/RatioMaster.app"
VERSION="$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$ROOT_DIR/Directory.Build.props" | head -1)"
VERSION="${VERSION:-0.44.0}"

say() { printf '\033[1;36m==>\033[0m %s\n' "$*"; }

if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: the .NET 8 SDK is required (https://dotnet.microsoft.com/download)" >&2
  exit 1
fi

# ---------------------------------------------------------------- publish ----

publish_rid() {
  local rid="$1"
  say "Publishing $rid"
  dotnet publish "$PROJECT" \
    --configuration Release \
    --runtime "$rid" \
    --self-contained true \
    --output "$OUT_DIR/publish-$rid" \
    -p:Version="$VERSION" \
    -p:PublishSingleFile=false \
    -p:DebugType=none \
    --nologo
}

rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR"

case "$ARCH" in
  arm64)      RIDS=(osx-arm64) ;;
  x86_64)     RIDS=(osx-x64) ;;
  universal)  RIDS=(osx-arm64 osx-x64) ;;
  *) echo "error: unknown architecture '$ARCH' (use arm64, x86_64 or universal)" >&2; exit 1 ;;
esac

for rid in "${RIDS[@]}"; do
  publish_rid "$rid"
done

# ------------------------------------------------------------- app bundle ----

say "Assembling RatioMaster.app"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"

PRIMARY="$OUT_DIR/publish-${RIDS[0]}"
cp -R "$PRIMARY/." "$APP_DIR/Contents/MacOS/"

if [[ ${#RIDS[@]} -eq 2 ]]; then
  say "Merging architectures with lipo"
  SECONDARY="$OUT_DIR/publish-${RIDS[1]}"

  # Every Mach-O file (the apphost plus the native runtime and Skia libraries)
  # gets fused; the managed assemblies are architecture independent already.
  while IFS= read -r -d '' file; do
    rel="${file#"$PRIMARY"/}"
    other="$SECONDARY/$rel"
    [[ -f "$other" ]] || continue
    file "$file" | grep -q 'Mach-O' || continue

    lipo -create "$file" "$other" -output "$APP_DIR/Contents/MacOS/$rel" 2>/dev/null \
      || cp "$file" "$APP_DIR/Contents/MacOS/$rel"
  done < <(find "$PRIMARY" -type f -print0)
fi

chmod +x "$APP_DIR/Contents/MacOS/RatioMaster"

# ----------------------------------------------------------------- icon ------

ICON_SRC="$REPO_DIR/icon.png"
if [[ -f "$ICON_SRC" ]] && command -v sips >/dev/null 2>&1; then
  say "Generating RatioMaster.icns"
  ICONSET="$OUT_DIR/RatioMaster.iconset"
  mkdir -p "$ICONSET"
  for size in 16 32 64 128 256 512; do
    sips -z $size $size        "$ICON_SRC" --out "$ICONSET/icon_${size}x${size}.png"     >/dev/null
    sips -z $((size*2)) $((size*2)) "$ICON_SRC" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
  done
  iconutil -c icns "$ICONSET" -o "$APP_DIR/Contents/Resources/RatioMaster.icns"
  rm -rf "$ICONSET"
else
  say "icon.png not found — the bundle will use the generic application icon"
fi

# --------------------------------------------------------------- Info.plist --

sed "s/__VERSION__/$VERSION/g" "$SCRIPT_DIR/Info.plist" > "$APP_DIR/Contents/Info.plist"
printf 'APPL????' > "$APP_DIR/Contents/PkgInfo"

# ------------------------------------------------------------------- sign ----

# An ad-hoc signature is enough for the app to launch on the machine that built
# it. Gatekeeper still asks the user to confirm the first run.
if command -v codesign >/dev/null 2>&1; then
  say "Signing (ad-hoc)"
  codesign --force --deep --sign - "$APP_DIR" 2>/dev/null || say "codesign failed — the app will still run after a right-click > Open"
fi

# -------------------------------------------------------------------- dmg ----

if $MAKE_DMG; then
  say "Building the disk image"
  STAGING="$OUT_DIR/dmg"
  mkdir -p "$STAGING"
  cp -R "$APP_DIR" "$STAGING/"
  ln -s /Applications "$STAGING/Applications"
  hdiutil create -volname "RatioMaster.NET $VERSION" \
    -srcfolder "$STAGING" -ov -format UDZO \
    "$OUT_DIR/RatioMaster-$VERSION-$ARCH.dmg" >/dev/null
  rm -rf "$STAGING"
fi

rm -rf "$OUT_DIR"/publish-*

say "Done: $APP_DIR"
if [[ -d "$APP_DIR" ]]; then
  file "$APP_DIR/Contents/MacOS/RatioMaster" | sed 's/^/    /'
  du -sh "$APP_DIR" | sed 's/^/    /'
fi
