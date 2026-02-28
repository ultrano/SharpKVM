#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_FILE="$ROOT_DIR/SharpKVM.csproj"
APP_NAME="SharpKVM"
VERSION="$(awk -F'[<>]' '/<Version>/{print $3; exit}' "$PROJECT_FILE")"
if [[ -z "$VERSION" ]]; then
  echo "Failed to read <Version> from $PROJECT_FILE"
  exit 1
fi
RUNTIME_ID="${1:-osx-arm64}"
PUBLISH_DIR="$ROOT_DIR/bin/Release/net8.0/$RUNTIME_ID/publish"
APP_DIR="$ROOT_DIR/bin/Release/${APP_NAME}.app"

echo "[1/3] Publishing for $RUNTIME_ID..."
dotnet publish "$PROJECT_FILE" \
  -c Release \
  -r "$RUNTIME_ID" \
  --self-contained true \
  /p:UseAppHost=true

echo "[2/3] Building .app bundle..."
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
cp -R "$PUBLISH_DIR"/. "$APP_DIR/Contents/MacOS/"
cp "$ROOT_DIR/AppIcon.icns" "$APP_DIR/Contents/Resources/AppIcon.icns"

cat > "$APP_DIR/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleExecutable</key>
  <string>$APP_NAME</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon.icns</string>
  <key>CFBundleIdentifier</key>
  <string>com.ultrano.sharpkvm</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>$APP_NAME</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$VERSION</string>
  <key>CFBundleVersion</key>
  <string>$VERSION</string>
  <key>LSMinimumSystemVersion</key>
  <string>11.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
EOF

chmod +x "$APP_DIR/Contents/MacOS/$APP_NAME"

echo "[3/3] Done."
echo "App bundle: $APP_DIR"
