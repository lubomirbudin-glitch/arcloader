#!/bin/zsh
# Builds build/Offload.app: SwiftUI app + offload CLI + bundled ffmpeg/ffprobe/braw-decode, the built-in LUTs and the icon.
# The LUTs and the icon come from the repo's shared/ folder (committed; Python is needed only to regenerate them).
# Optional label: `scripts/build-app.sh b` builds build/Offload-b.app named "Offload b" (version 0.x.yb) next to the
# normal build, which stays untouched (it may be running).
set -euo pipefail
LABEL=${1:-}
cd "$(dirname "$0")/.."
SHARED=../shared
if [[ ! -f $SHARED/luts/luts.json ]]; then
    echo "error: $SHARED/luts/luts.json missing — build from a full clone of the repo (mac/ and shared/)"
    exit 1
fi
swift build -c release --product OffloadApp
swift build -c release --product offload
[[ -x Tools/bin/braw-decode ]] || Tools/braw-decode/build.sh

APP=build/Offload${LABEL:+-$LABEL}.app
NAME="Offload${LABEL:+ $LABEL}"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/bin" "$APP/Contents/Resources/luts"
cp .build/release/OffloadApp "$APP/Contents/MacOS/Offload"
cp .build/release/offload "$APP/Contents/Resources/bin/offload"
for t in ffmpeg ffprobe braw-decode; do
    [[ -x Tools/bin/$t ]] && cp Tools/bin/$t "$APP/Contents/Resources/bin/" || echo "warning: Tools/bin/$t missing"
done
# Built-in LUTs: the app finds Resources/luts, the CLI in Resources/bin finds ../luts (LutCatalog.findFolder).
cp $SHARED/luts/luts.json $SHARED/luts/*.cube "$APP/Contents/Resources/luts/"
# App icon from the shared iconset (16–512 px, @1x and @2x).
if [[ -d $SHARED/icon/Offload.iconset ]]; then
    iconutil -c icns $SHARED/icon/Offload.iconset -o "$APP/Contents/Resources/Offload.icns"
else
    echo "warning: $SHARED/icon/Offload.iconset missing — the app has no icon"
fi
VERSION=$(sed -n 's/.*static let string = "\(.*\)".*/\1/p' Sources/OffloadKit/Report.swift)
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>$NAME</string>
  <key>CFBundleDisplayName</key><string>$NAME</string>
  <key>CFBundleIdentifier</key><string>cz.budin.offload</string>
  <key>CFBundleExecutable</key><string>Offload</string>
  <key>CFBundleIconFile</key><string>Offload</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION$LABEL</string>
  <key>CFBundleVersion</key><string>$VERSION$LABEL</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>NSRemovableVolumesUsageDescription</key><string>Offload čte záznam z paměťových karet.</string>
  <key>NSNetworkVolumesUsageDescription</key><string>Offload může zapisovat na síťové disky.</string>
</dict></plist>
PLIST
codesign --force --deep --sign - "$APP" 2>/dev/null || true
echo "built $APP"
