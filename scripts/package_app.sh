#!/bin/sh
set -eu

PROJECT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
OUTPUT_DIR="$PROJECT_DIR/dist"
APP_DIR="${WORK_ISLAND_BUILD_APP:-$OUTPUT_DIR/Work Island.app}"
# Refuse broad or installed targets before rebuilding a disposable bundle.
case "$APP_DIR" in
    /*/Work\ Island.app) ;;
    *) echo "Build target must be an absolute path ending in /Work Island.app" >&2; exit 1 ;;
esac
if [ -L "$APP_DIR" ]; then
    echo "Use update_app.sh to replace an installed application" >&2
    exit 1
fi
mkdir -p "$(dirname -- "$APP_DIR")"
APP_DIR="$(CDPATH= cd -- "$(dirname -- "$APP_DIR")" && pwd -P)/Work Island.app"
case "$APP_DIR" in
    */Applications/Work\ Island.app)
        echo "Use update_app.sh to replace an installed application" >&2
        exit 1 ;;
esac
if [ -e "$APP_DIR" ] && [ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$APP_DIR/Contents/Info.plist" 2>/dev/null || true)" != com.focusisland.app ]; then
    echo "Build target is not a generated Work Island bundle; nothing changed" >&2
    exit 1
fi
CACHE_DIR="$PROJECT_DIR/.build/work-island-cache"
CACHE_KEY=$(printf '%s' "$PROJECT_DIR" | shasum -a 256 | cut -c 1-12)
BUILD_DIR="$CACHE_DIR/release-build-$CACHE_KEY"
DMG_STAGE="$CACHE_DIR/dmg-stage"
VERSION=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$PROJECT_DIR/AppResources/Info.plist")
DMG_PATH="$OUTPUT_DIR/Work Island $VERSION.dmg"

export DEVELOPER_DIR="${DEVELOPER_DIR:-$(xcode-select -p)}"
export CLANG_MODULE_CACHE_PATH="${CLANG_MODULE_CACHE_PATH:-$CACHE_DIR/clang-cache-$CACHE_KEY}"
export SWIFTPM_MODULECACHE_OVERRIDE="${SWIFTPM_MODULECACHE_OVERRIDE:-$CACHE_DIR/swiftpm-cache-$CACHE_KEY}"

mkdir -p "$CLANG_MODULE_CACHE_PATH" "$SWIFTPM_MODULECACHE_OVERRIDE"

cd "$PROJECT_DIR"
xcrun swift build -c release --disable-sandbox --scratch-path "$BUILD_DIR"

rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
cp "$BUILD_DIR/release/FocusIsland" "$APP_DIR/Contents/MacOS/FocusIsland"
cp "$PROJECT_DIR/AppResources/Info.plist" "$APP_DIR/Contents/Info.plist"
cp "$PROJECT_DIR/AppResources/FocusIsland.icns" "$APP_DIR/Contents/Resources/FocusIsland.icns"
cp "$PROJECT_DIR/使用教程.html" "$APP_DIR/Contents/Resources/使用教程.html"
cp "$PROJECT_DIR/THIRD_PARTY_NOTICES.md" "$APP_DIR/Contents/Resources/THIRD_PARTY_NOTICES.md"
for LOCALE in en zh-Hans; do
    mkdir -p "$APP_DIR/Contents/Resources/$LOCALE.lproj"
    cp "$PROJECT_DIR/AppResources/$LOCALE.lproj/InfoPlist.strings" \
        "$APP_DIR/Contents/Resources/$LOCALE.lproj/InfoPlist.strings"
done

# A local, ignored identity keeps development permission identity stable.
# Public/CI builds retain ad-hoc signing unless explicitly configured.
SIGNING_IDENTITY="${WORK_ISLAND_SIGNING_IDENTITY:-}"
if [ -z "$SIGNING_IDENTITY" ] && [ -f "$PROJECT_DIR/.work-island-signing-identity" ]; then
    SIGNING_IDENTITY=$(tr -d '\r\n' < "$PROJECT_DIR/.work-island-signing-identity")
fi
SIGNING_IDENTITY="${SIGNING_IDENTITY:--}"
codesign --force --deep --sign "$SIGNING_IDENTITY" "$APP_DIR"
codesign --verify --deep --strict "$APP_DIR"

rm -rf "$DMG_STAGE"
mkdir -p "$DMG_STAGE"
cp -R "$APP_DIR" "$DMG_STAGE/Work Island.app"
cp "$PROJECT_DIR/使用教程.html" "$DMG_STAGE/使用教程.html"
ln -s /Applications "$DMG_STAGE/Applications"
if [ -e "$DMG_PATH" ]; then
    DMG_PATH="$OUTPUT_DIR/Work Island $VERSION-$(date +%Y%m%d-%H%M%S).dmg"
fi
hdiutil create -volname "Work Island" -srcfolder "$DMG_STAGE" -format UDZO "$DMG_PATH" >/dev/null
printf '%s\n' "$DMG_PATH" > "$CACHE_DIR/latest-dmg-path"

echo "$APP_DIR"
echo "$DMG_PATH"
