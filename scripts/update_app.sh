#!/bin/sh
set -eu
PROJECT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
# Restore an already released and signed bundle without rebuilding/re-signing
# it. Keep its exact permission identity and use the same atomic restart path.
if [ "$#" -gt 0 ]; then
    if [ "$#" -ne 2 ] || [ "$1" != "--restore-app" ]; then
        echo 'Usage: update_app.sh [--restore-app /absolute/path/Work Island.app]' >&2
        exit 1
    fi
    RESTORE_APP="$2"
    case "$RESTORE_APP" in
        /Applications/Work\ Island.app) echo 'Restore source must be a saved bundle, not the installed app' >&2; exit 1 ;;
        /*/Work\ Island.app) ;;
        *) echo 'Restore source must be an absolute path ending in /Work Island.app' >&2; exit 1 ;;
    esac
    if [ ! -d "$RESTORE_APP" ] || [ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$RESTORE_APP/Contents/Info.plist" 2>/dev/null || true)" != com.focusisland.app ]; then
        echo 'Restore source is not a saved Work Island bundle; nothing changed' >&2
        exit 1
    fi
    codesign --verify --deep --strict "$RESTORE_APP"
    "$PROJECT_DIR/scripts/test_app.sh"
    xcrun swift "$PROJECT_DIR/scripts/restart_app.swift" "$RESTORE_APP"
    exit 0
fi
"$PROJECT_DIR/scripts/test_app.sh"
UPDATE_STAGE=$(mktemp -d /private/tmp/work-island-update.XXXXXX)
trap 'rm -rf "$UPDATE_STAGE"' EXIT HUP INT TERM
export WORK_ISLAND_BUILD_APP="$UPDATE_STAGE/Work Island.app"
"$PROJECT_DIR/scripts/package_app.sh"
DMG_PATH=$(cat "$PROJECT_DIR/.build/work-island-cache/latest-dmg-path")
hdiutil verify "$DMG_PATH"
xcrun swift "$PROJECT_DIR/scripts/restart_app.swift" "$WORK_ISLAND_BUILD_APP"
# Only generated copies of this verified product are removed after a successful upgrade.
if [ -d "$PROJECT_DIR/dist/Work Island.app" ] && [ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$PROJECT_DIR/dist/Work Island.app/Contents/Info.plist")" = com.focusisland.app ]; then
    rm -rf "$PROJECT_DIR/dist/Work Island.app"
fi
rm -rf "$PROJECT_DIR/.build/work-island-cache/dmg-stage"
