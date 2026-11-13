#!/bin/sh
set -eu

PROJECT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
CACHE_DIR="$PROJECT_DIR/.build/work-island-cache"
CACHE_KEY=$(printf '%s' "$PROJECT_DIR" | shasum -a 256 | cut -c 1-12)

export DEVELOPER_DIR="${DEVELOPER_DIR:-$(xcode-select -p)}"
export CLANG_MODULE_CACHE_PATH="$CACHE_DIR/clang-cache-$CACHE_KEY"
export SWIFTPM_MODULECACHE_OVERRIDE="$CACHE_DIR/swiftpm-cache-$CACHE_KEY"

mkdir -p "$CLANG_MODULE_CACHE_PATH" "$SWIFTPM_MODULECACHE_OVERRIDE"
cd "$PROJECT_DIR"
xcrun swift test --disable-sandbox --scratch-path "$CACHE_DIR/test-build-$CACHE_KEY" "$@"
