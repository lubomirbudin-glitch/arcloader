#!/bin/zsh
# Builds build/Offload.app: SwiftUI app + offload CLI + bundled ffmpeg/ffprobe/braw-decode.
set -euo pipefail
cd "$(dirname "$0")/.."
swift build -c release --product OffloadApp
swift build -c release --product offload
[[ -x Tools/bin/braw-decode ]] || Tools/braw-decode/build.sh

APP=build/Offload.app
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/bin"
cp .build/release/OffloadApp "$APP/Contents/MacOS/Offload"
cp .build/release/offload "$APP/Contents/Resources/bin/offload"
for t in ffmpeg ffprobe braw-decode; do
    [[ -x Tools/bin/$t ]] && cp Tools/bin/$t "$APP/Contents/Resources/bin/" || echo "warning: Tools/bin/$t missing"
done
VERSION=$(sed -n 's/.*static let string = "\(.*\)".*/\1/p' Sources/OffloadKit/Report.swift)
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>Offload</string>
  <key>CFBundleDisplayName</key><string>Offload</string>
  <key>CFBundleIdentifier</key><string>cz.budin.offload</string>
  <key>CFBundleExecutable</key><string>Offload</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>NSRemovableVolumesUsageDescription</key><string>Offload čte záznam z paměťových karet.</string>
  <key>NSNetworkVolumesUsageDescription</key><string>Offload může zapisovat na síťové disky.</string>
</dict></plist>
PLIST
codesign --force --deep --sign - "$APP" 2>/dev/null || true
echo "built $APP"
