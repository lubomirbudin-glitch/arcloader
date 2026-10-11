#!/bin/zsh
# Builds build/ArcLoader.app: SwiftUI app + arcloader CLI + bundled ffmpeg/ffprobe/braw-decode, the built-in LUTs and the
# icon. The LUTs and the icon come from the repo's shared/ folder (committed; Python is needed only to regenerate them).
# Optional label: `scripts/build-app.sh b` builds build/ArcLoader-b.app named "ArcLoader b" (version 0.x.yb).
# Never overwrites or deletes a build that is there (one may be running, an older one is the way back): a label that is
# taken stops the script — choose a new one. Builds from before the rename (build/Offload*.app) are never touched.
# The bundle id stays cz.budin.offload: the app's settings (UserDefaults) carry over from the Offload builds.
set -euo pipefail
LABEL=${1:-}
cd "$(dirname "$0")/.."
SHARED=../shared
if [[ ! -f $SHARED/luts/luts.json ]]; then
    echo "error: $SHARED/luts/luts.json missing — build from a full clone of the repo (mac/ and shared/)"
    exit 1
fi
APP=build/ArcLoader${LABEL:+-$LABEL}.app
NAME="ArcLoader${LABEL:+ $LABEL}"
if [[ -e $APP ]]; then
    echo "error: $APP exists — it is never overwritten; use a new label (scripts/build-app.sh <label>)"
    exit 1
fi
swift build -c release --product OffloadApp
swift build -c release --product arcloader
[[ -x Tools/bin/braw-decode ]] || Tools/braw-decode/build.sh

# Assembled under a temporary name and renamed at the end: a build cut short never looks like a finished one. Only this
# script's own temporary folder is ever removed.
STAGE=build/.partial-ArcLoader${LABEL:+-$LABEL}.app
rm -rf "$STAGE"
mkdir -p "$STAGE/Contents/MacOS" "$STAGE/Contents/Resources/bin" "$STAGE/Contents/Resources/luts"
cp .build/release/OffloadApp "$STAGE/Contents/MacOS/ArcLoader"
cp .build/release/arcloader "$STAGE/Contents/Resources/bin/arcloader"
for t in ffmpeg ffprobe braw-decode; do
    [[ -x Tools/bin/$t ]] && cp Tools/bin/$t "$STAGE/Contents/Resources/bin/" || echo "warning: Tools/bin/$t missing"
done
# Built-in LUTs: the app finds Resources/luts, the CLI in Resources/bin finds ../luts (LutCatalog.findFolder).
cp $SHARED/luts/luts.json $SHARED/luts/*.cube "$STAGE/Contents/Resources/luts/"
# App icon from the shared iconset (16–512 px, @1x and @2x).
if [[ -d $SHARED/icon/Offload.iconset ]]; then
    iconutil -c icns $SHARED/icon/Offload.iconset -o "$STAGE/Contents/Resources/ArcLoader.icns"
else
    echo "warning: $SHARED/icon/Offload.iconset missing — the app has no icon"
fi
VERSION=$(sed -n 's/.*static let string = "\(.*\)".*/\1/p' Sources/OffloadKit/Report.swift)
cat > "$STAGE/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>$NAME</string>
  <key>CFBundleDisplayName</key><string>$NAME</string>
  <key>CFBundleIdentifier</key><string>cz.budin.offload</string>
  <key>CFBundleExecutable</key><string>ArcLoader</string>
  <key>CFBundleIconFile</key><string>ArcLoader</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION$LABEL</string>
  <key>CFBundleVersion</key><string>$VERSION$LABEL</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>NSRemovableVolumesUsageDescription</key><string>ArcLoader reads the footage from memory cards.</string>
  <key>NSNetworkVolumesUsageDescription</key><string>ArcLoader can write to network drives.</string>
</dict></plist>
PLIST
codesign --force --deep --sign - "$STAGE" 2>/dev/null || true
# The final name only now, and never over anything (mv -n; checked again — another build may have taken it meanwhile).
if [[ -e $APP ]]; then
    echo "error: $APP appeared during the build — left as it was; this build is in $STAGE"
    exit 1
fi
mv -n "$STAGE" "$APP"
echo "built $APP"
