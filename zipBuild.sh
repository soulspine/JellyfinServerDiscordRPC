#!/usr/bin/env bash
set -e

SOURCE="bin/Release/net9.0"
OUTPUT="discordRPC-1.0.0.zip"

START_DIR="$(pwd)"

if [ ! -d "$SOURCE" ]; then
    echo "Folder not found: $SOURCE"
    exit 1
fi

echo "Packing DLL files from $SOURCE..."

cd "$SOURCE"
zip -r "$START_DIR/$OUTPUT" *.dll

cd "$START_DIR"

echo "Done! Created $OUTPUT"
